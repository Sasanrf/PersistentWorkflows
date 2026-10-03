using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PersistentWorkflows.Abstractions.Execution;
using PersistentWorkflows.Abstractions.Persistence;
using PersistentWorkflows.Abstractions.Serialization;
using PersistentWorkflows.Core.DependencyInjection;
using PersistentWorkflows.Core.Execution;
using PersistentWorkflows.Core.Serialization;
using PersistentWorkflows.Core.Tests.TestDoubles;
using Xunit;

namespace PersistentWorkflows.Core.Tests.Execution;

public sealed class WorkflowRunnerTests
{
    [Fact]
    public async Task StartAsync_Should_Execute_All_Steps_In_Order_When_All_Steps_Succeed()
    {
        using var provider = BuildServiceProvider(services =>
        {
            services.AddScoped<FirstRunnerStep>();
            services.AddScoped<SecondRunnerStep>();
            services.AddScoped<SuccessfulRunnerWorkflow>();
            services.AddWorkflow<SuccessfulRunnerWorkflow, RunnerWorkflowContext>();
        });

        var runner = provider.GetRequiredService<IWorkflowRunner>();
        var trace = provider.GetRequiredService<ExecutionTrace>();
        var context = new RunnerWorkflowContext();

        var result = await runner.StartAsync<SuccessfulRunnerWorkflow, RunnerWorkflowContext>(
            "instance-1",
            context);

        result.AlreadyExists.Should().BeFalse();
        result.WorkflowInstanceId.Should().NotBe(Guid.Empty);
        trace.Entries.Should().Equal("step-1", "step-2");
        var instanceRepository = provider.GetRequiredService<IWorkflowInstanceRepository>() as FakeWorkflowInstanceRepository;
        instanceRepository.Should().NotBeNull();

        var savedInstance = instanceRepository!.Instances.Single();

        savedInstance.Status.Should().Be("Succeeded");
        savedInstance.CurrentStepIndex.Should().Be(2);
        savedInstance.ContextJson.Should().Contain("step-1");
        savedInstance.ContextJson.Should().Contain("step-2");
    }

    [Fact]
    public async Task StartAsync_Should_Stop_When_A_Step_Returns_Wait()
    {
        using var provider = BuildServiceProvider(services =>
        {
            services.AddScoped<FirstRunnerStep>();
            services.AddScoped<WaitingRunnerStep>();
            services.AddScoped<SecondRunnerStep>();
            services.AddScoped<WaitingRunnerWorkflow>();
            services.AddWorkflow<WaitingRunnerWorkflow, RunnerWorkflowContext>();
        });

        var runner = provider.GetRequiredService<IWorkflowRunner>();
        var context = new RunnerWorkflowContext();

        var result = await runner.StartAsync<WaitingRunnerWorkflow, RunnerWorkflowContext>(
            "instance-1",
            context);

        result.AlreadyExists.Should().BeFalse();
        var instanceRepository = provider.GetRequiredService<IWorkflowInstanceRepository>() as FakeWorkflowInstanceRepository;
        instanceRepository.Should().NotBeNull();

        var savedInstance = instanceRepository!.Instances.Single();

        savedInstance.Status.Should().Be("Waiting");
        savedInstance.CurrentStepIndex.Should().Be(1);
        savedInstance.ContextJson.Should().Contain("step-1");
        savedInstance.ContextJson.Should().Contain("waiting-step");
    }

    [Fact]
    public async Task StartAsync_Should_Stop_When_A_Step_Returns_Fail()
    {
        using var provider = BuildServiceProvider(services =>
        {
            services.AddScoped<FirstRunnerStep>();
            services.AddScoped<FailingRunnerStep>();
            services.AddScoped<SecondRunnerStep>();
            services.AddScoped<FailingRunnerWorkflow>();
            services.AddWorkflow<FailingRunnerWorkflow, RunnerWorkflowContext>();
        });

        var runner = provider.GetRequiredService<IWorkflowRunner>();
        var context = new RunnerWorkflowContext();

        var result = await runner.StartAsync<FailingRunnerWorkflow, RunnerWorkflowContext>(
            "instance-1",
            context);

        result.AlreadyExists.Should().BeFalse();
        var instanceRepository = provider.GetRequiredService<IWorkflowInstanceRepository>() as FakeWorkflowInstanceRepository;
        instanceRepository.Should().NotBeNull();

        var savedInstance = instanceRepository!.Instances.Single();

        savedInstance.Status.Should().Be("Failed");
        savedInstance.CurrentStepIndex.Should().Be(1);
        savedInstance.ContextJson.Should().Contain("step-1");
        savedInstance.ContextJson.Should().Contain("failing-step");
    }

    [Fact]
    public async Task StartAsync_Should_Return_AlreadyExists_When_Same_InstanceKey_Is_Used_Again()
    {
        using var provider = BuildServiceProvider(services =>
        {
            services.AddScoped<FirstRunnerStep>();
            services.AddScoped<SecondRunnerStep>();
            services.AddWorkflow<SuccessfulRunnerWorkflow, RunnerWorkflowContext>();
        });

        var runner = provider.GetRequiredService<IWorkflowRunner>();

        var firstContext = new RunnerWorkflowContext();
        var secondContext = new RunnerWorkflowContext();

        var firstResult = await runner.StartAsync<SuccessfulRunnerWorkflow, RunnerWorkflowContext>(
            "same-key",
            firstContext);

        var secondResult = await runner.StartAsync<SuccessfulRunnerWorkflow, RunnerWorkflowContext>(
            "same-key",
            secondContext);

        firstResult.AlreadyExists.Should().BeFalse();
        secondResult.AlreadyExists.Should().BeTrue();
        secondResult.WorkflowInstanceId.Should().Be(firstResult.WorkflowInstanceId);
        secondContext.ExecutedSteps.Should().BeEmpty();
    }

    private static ServiceProvider BuildServiceProvider(Action<IServiceCollection> configureServices)
    {
        var services = new ServiceCollection();

        services.AddSingleton<ExecutionTrace>();

        services.AddSingleton<IWorkflowInstanceRepository, FakeWorkflowInstanceRepository>();
        services.AddSingleton<IWorkflowStepExecutionRepository, FakeWorkflowStepExecutionRepository>();
        services.AddSingleton<IWorkflowContextSerializer, SystemTextJsonWorkflowContextSerializer>();

        services.AddPersistentWorkflows();
        services.AddSingleton<IWorkflowStore, FakeWorkflowStore>();

        configureServices(services);

        return services.BuildServiceProvider();
    }
}
