using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PersistentWorkflows.Abstractions.Execution;
using PersistentWorkflows.Abstractions.Serialization;
using PersistentWorkflows.Core.DependencyInjection;
using PersistentWorkflows.Core.Execution;
using PersistentWorkflows.EntityFrameworkCore.Persistence;
using PersistentWorkflows.EntityFrameworkCore.Tests.TestDoubles;
using Xunit;

namespace PersistentWorkflows.EntityFrameworkCore.Tests.Execution;

public sealed class WorkflowRunnerPersistenceTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public WorkflowRunnerPersistenceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    [Fact]
    public async Task StartAsync_Should_Persist_Workflow_And_Step_Executions_When_Steps_Succeed()
    {
        using var provider = BuildServiceProvider(services =>
        {
            services.AddScoped<FirstRunnerStep>();
            services.AddScoped<SecondRunnerStep>();
            services.AddWorkflow<SuccessfulRunnerWorkflow, RunnerWorkflowContext>();
        });

        await EnsureDatabaseCreated(provider);

        var runner = provider.GetRequiredService<IWorkflowRunner>();
        var dbContext = provider.GetRequiredService<PersistentWorkflowsDbContext>();
        var trace = provider.GetRequiredService<ExecutionTrace>();
        var serializer = provider.GetRequiredService<IWorkflowContextSerializer>();

        var result = await runner.StartAsync<SuccessfulRunnerWorkflow, RunnerWorkflowContext>(
            "instance-1",
            new RunnerWorkflowContext());

        result.AlreadyExists.Should().BeFalse();
        trace.Entries.Should().Equal("step-1", "step-2");

        var instance = await dbContext.WorkflowInstances.SingleAsync();

        instance.Id.Should().Be(result.WorkflowInstanceId);
        instance.WorkflowName.Should().Be("successful-runner-workflow");
        instance.InstanceKey.Should().Be("instance-1");
        instance.Status.Should().Be("Succeeded");
        instance.CurrentStepIndex.Should().Be(2);

        var savedContext = serializer.Deserialize<RunnerWorkflowContext>(instance.ContextJson);
        savedContext.ExecutedSteps.Should().Equal("step-1", "step-2");

        var stepExecutions = await dbContext.WorkflowStepExecutions
            .OrderBy(x => x.StartedAtUtc)
            .ToListAsync();

        stepExecutions.Should().HaveCount(2);
        stepExecutions[0].StepName.Should().Be("step-1");
        stepExecutions[0].Status.Should().Be("Succeeded");
        stepExecutions[1].StepName.Should().Be("step-2");
        stepExecutions[1].Status.Should().Be("Succeeded");
    }

    [Fact]
    public async Task StartAsync_Should_Return_AlreadyExists_When_Same_Key_Is_Used_Again()
    {
        using var provider = BuildServiceProvider(services =>
        {
            services.AddScoped<FirstRunnerStep>();
            services.AddScoped<SecondRunnerStep>();
            services.AddWorkflow<SuccessfulRunnerWorkflow, RunnerWorkflowContext>();
        });

        await EnsureDatabaseCreated(provider);

        var runner = provider.GetRequiredService<IWorkflowRunner>();

        var first = await runner.StartAsync<SuccessfulRunnerWorkflow, RunnerWorkflowContext>(
            "same-key",
            new RunnerWorkflowContext());

        var second = await runner.StartAsync<SuccessfulRunnerWorkflow, RunnerWorkflowContext>(
            "same-key",
            new RunnerWorkflowContext());

        first.AlreadyExists.Should().BeFalse();
        second.AlreadyExists.Should().BeTrue();
        second.WorkflowInstanceId.Should().Be(first.WorkflowInstanceId);
    }

    [Fact]
    public async Task StartAsync_Should_Persist_Waiting_Status_When_Step_Returns_Wait()
    {
        using var provider = BuildServiceProvider(services =>
        {
            services.AddScoped<FirstRunnerStep>();
            services.AddScoped<WaitingRunnerStep>();
            services.AddScoped<SecondRunnerStep>();
            services.AddWorkflow<WaitingRunnerWorkflow, RunnerWorkflowContext>();
        });

        await EnsureDatabaseCreated(provider);

        var runner = provider.GetRequiredService<IWorkflowRunner>();
        var dbContext = provider.GetRequiredService<PersistentWorkflowsDbContext>();
        var serializer = provider.GetRequiredService<IWorkflowContextSerializer>();

        var result = await runner.StartAsync<WaitingRunnerWorkflow, RunnerWorkflowContext>(
            "waiting-instance",
            new RunnerWorkflowContext());

        result.AlreadyExists.Should().BeFalse();

        var instance = await dbContext.WorkflowInstances.SingleAsync();

        instance.Status.Should().Be("Waiting");
        instance.CurrentStepIndex.Should().Be(1);
        instance.NextExecutionAtUtc.Should().NotBeNull();

        var savedContext = serializer.Deserialize<RunnerWorkflowContext>(instance.ContextJson);
        savedContext.ExecutedSteps.Should().Equal("step-1", "waiting-step");

        var stepExecutions = await dbContext.WorkflowStepExecutions
            .OrderBy(x => x.StartedAtUtc)
            .ToListAsync();

        stepExecutions.Should().HaveCount(2);
        stepExecutions[0].Status.Should().Be("Succeeded");
        stepExecutions[1].Status.Should().Be("Waiting");
    }

    [Fact]
    public async Task ResumeAsync_Should_Continue_A_Waiting_Workflow()
    {
        using var provider = BuildServiceProvider(services =>
        {
            services.AddScoped<FirstRunnerStep>();
            services.AddScoped<ResumableRunnerStep>();
            services.AddScoped<SecondRunnerStep>();
            services.AddWorkflow<ResumableRunnerWorkflow, RunnerWorkflowContext>();
        });

        await EnsureDatabaseCreated(provider);

        var runner = provider.GetRequiredService<IWorkflowRunner>();
        var dbContext = provider.GetRequiredService<PersistentWorkflowsDbContext>();
        var serializer = provider.GetRequiredService<IWorkflowContextSerializer>();

        var startResult = await runner.StartAsync<ResumableRunnerWorkflow, RunnerWorkflowContext>(
            "resume-instance",
            new RunnerWorkflowContext());

        var startedInstance = await dbContext.WorkflowInstances.SingleAsync();
        startedInstance.Status.Should().Be("Waiting");
        startedInstance.CurrentStepIndex.Should().Be(1);

        await runner.ResumeAsync(startResult.WorkflowInstanceId);

        var resumedInstance = await dbContext.WorkflowInstances.SingleAsync();

        resumedInstance.Status.Should().Be("Succeeded");
        resumedInstance.CurrentStepIndex.Should().Be(3);
        resumedInstance.NextExecutionAtUtc.Should().BeNull();

        var savedContext = serializer.Deserialize<RunnerWorkflowContext>(resumedInstance.ContextJson);
        savedContext.ExecutedSteps.Should().Equal("step-1", "resumable-wait", "resumable-success", "step-2");

        var stepExecutions = await dbContext.WorkflowStepExecutions
            .OrderBy(x => x.StartedAtUtc)
            .ThenBy(x => x.Attempt)
            .ToListAsync();

        stepExecutions.Should().HaveCount(4);
    }

    [Fact]
    public async Task StartAsync_Should_Return_AlreadyExists_When_Duplicate_Insert_Happens()
    {
        using var provider = BuildServiceProvider(services =>
        {
            services.AddScoped<FirstRunnerStep>();
            services.AddScoped<SecondRunnerStep>();
            services.AddWorkflow<SuccessfulRunnerWorkflow, RunnerWorkflowContext>();
        });

        await EnsureDatabaseCreated(provider);

        var runner = provider.GetRequiredService<IWorkflowRunner>();
        var dbContext = provider.GetRequiredService<PersistentWorkflowsDbContext>();

        var first = await runner.StartAsync<SuccessfulRunnerWorkflow, RunnerWorkflowContext>(
            "duplicate-key",
            new RunnerWorkflowContext());

        var second = await runner.StartAsync<SuccessfulRunnerWorkflow, RunnerWorkflowContext>(
            "duplicate-key",
            new RunnerWorkflowContext());

        first.AlreadyExists.Should().BeFalse();
        second.AlreadyExists.Should().BeTrue();
        second.WorkflowInstanceId.Should().Be(first.WorkflowInstanceId);

        var instances = await dbContext.WorkflowInstances.ToListAsync();
        instances.Should().HaveCount(1);
    }

    [Fact]
    public async Task ResumeAsync_Should_Not_Run_A_Succeeded_Workflow_Again()
    {
        using var provider = BuildServiceProvider(services =>
        {
            services.AddScoped<FirstRunnerStep>();
            services.AddScoped<SecondRunnerStep>();
            services.AddWorkflow<SuccessfulRunnerWorkflow, RunnerWorkflowContext>();
        });

        await EnsureDatabaseCreated(provider);

        var runner = provider.GetRequiredService<IWorkflowRunner>();
        var dbContext = provider.GetRequiredService<PersistentWorkflowsDbContext>();

        var result = await runner.StartAsync<SuccessfulRunnerWorkflow, RunnerWorkflowContext>(
            "resume-succeeded",
            new RunnerWorkflowContext());

        await runner.ResumeAsync(result.WorkflowInstanceId);

        var stepExecutions = await dbContext.WorkflowStepExecutions.ToListAsync();
        stepExecutions.Should().HaveCount(2);
    }


    private ServiceProvider BuildServiceProvider(Action<IServiceCollection> configureServices)
    {
        var services = new ServiceCollection();

        services.AddLogging();

        services.AddSingleton<ExecutionTrace>();

        services.AddPersistentWorkflows();
        services.AddPersistentWorkflowsEntityFrameworkCore(options =>
        {
            options.UseSqlite(_connection);
        });

        configureServices(services);

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task ResumeAsync_Should_Throw_When_Workflow_Instance_Does_Not_Exist()
    {
        using var provider = BuildServiceProvider(services =>
        {
            services.AddScoped<FirstRunnerStep>();
            services.AddScoped<SecondRunnerStep>();
            services.AddWorkflow<SuccessfulRunnerWorkflow, RunnerWorkflowContext>();
        });

        await EnsureDatabaseCreated(provider);

        var runner = provider.GetRequiredService<IWorkflowRunner>();

        var action = async () => await runner.ResumeAsync(Guid.NewGuid());

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*was not found*");
    }

    [Fact]
    public async Task ResumeAsync_Should_Do_Nothing_When_Workflow_Is_Already_Succeeded()
    {
        using var provider = BuildServiceProvider(services =>
        {
            services.AddScoped<FirstRunnerStep>();
            services.AddScoped<SecondRunnerStep>();
            services.AddWorkflow<SuccessfulRunnerWorkflow, RunnerWorkflowContext>();
        });

        await EnsureDatabaseCreated(provider);

        var runner = provider.GetRequiredService<IWorkflowRunner>();
        var dbContext = provider.GetRequiredService<PersistentWorkflowsDbContext>();

        var result = await runner.StartAsync<SuccessfulRunnerWorkflow, RunnerWorkflowContext>(
            "resume-succeeded",
            new RunnerWorkflowContext());

        await runner.ResumeAsync(result.WorkflowInstanceId);

        var stepExecutions = await dbContext.WorkflowStepExecutions.ToListAsync();
        stepExecutions.Should().HaveCount(2);
    }

    [Fact]
    public async Task ResumeAsync_Should_Do_Nothing_When_Workflow_Is_Already_Failed()
    {
        using var provider = BuildServiceProvider(services =>
        {
            services.AddScoped<FirstRunnerStep>();
            services.AddScoped<FailingRunnerStep>();
            services.AddScoped<SecondRunnerStep>();
            services.AddWorkflow<FailingRunnerWorkflow, RunnerWorkflowContext>();
        });

        await EnsureDatabaseCreated(provider);

        var runner = provider.GetRequiredService<IWorkflowRunner>();
        var dbContext = provider.GetRequiredService<PersistentWorkflowsDbContext>();

        var result = await runner.StartAsync<FailingRunnerWorkflow, RunnerWorkflowContext>(
            "resume-failed",
            new RunnerWorkflowContext());

        await runner.ResumeAsync(result.WorkflowInstanceId);

        var stepExecutions = await dbContext.WorkflowStepExecutions.ToListAsync();
        stepExecutions.Should().HaveCount(2);

        var instance = await dbContext.WorkflowInstances.SingleAsync();
        instance.Status.Should().Be("Failed");
    }

    [Fact]
    public async Task BackgroundResumer_Should_Resume_Due_Waiting_Workflows()
    {
        using var provider = BuildServiceProvider(services =>
        {
            services.AddScoped<FirstRunnerStep>();
            services.AddScoped<ResumableRunnerStep>();
            services.AddScoped<SecondRunnerStep>();

            services.AddWorkflow<ResumableRunnerWorkflow, RunnerWorkflowContext>();

            services.AddPersistentWorkflowsBackgroundResumer(options =>
            {
                options.PollingInterval = TimeSpan.FromMilliseconds(50);
                options.BatchSize = 10;
            });
        });

        await EnsureDatabaseCreated(provider);

        var runner = provider.GetRequiredService<IWorkflowRunner>();
        var dbContext = provider.GetRequiredService<PersistentWorkflowsDbContext>();
        var resumer = provider.GetRequiredService<IWorkflowResumer>();

        var result = await runner.StartAsync<ResumableRunnerWorkflow, RunnerWorkflowContext>(
            "background-resume-instance",
            new RunnerWorkflowContext());

        var instance = await dbContext.WorkflowInstances.SingleAsync();

        instance.Status.Should().Be("Waiting");

        instance.NextExecutionAtUtc = DateTime.UtcNow.AddSeconds(-1);
        await dbContext.SaveChangesAsync();

        await resumer.ProcessOnceAsync(CancellationToken.None);

        dbContext.ChangeTracker.Clear();

        var finalInstance = await dbContext.WorkflowInstances.SingleAsync();

        finalInstance.Id.Should().Be(result.WorkflowInstanceId);
        finalInstance.Status.Should().Be("Succeeded");
        finalInstance.CurrentStepIndex.Should().Be(3);
    }

    private static async Task EnsureDatabaseCreated(ServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PersistentWorkflowsDbContext>();
        await dbContext.Database.EnsureCreatedAsync();
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}