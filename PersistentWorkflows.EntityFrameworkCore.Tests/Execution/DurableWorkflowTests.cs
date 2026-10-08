using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PersistentWorkflows.Abstractions.Definitions;
using PersistentWorkflows.Abstractions.Enums;
using PersistentWorkflows.Abstractions.Execution;
using PersistentWorkflows.Abstractions.Persistence;
using PersistentWorkflows.Abstractions.Results;
using PersistentWorkflows.Abstractions.Serialization;
using PersistentWorkflows.Core.DependencyInjection;
using PersistentWorkflows.Core.Execution;
using PersistentWorkflows.EntityFrameworkCore.Persistence;
using Xunit;

namespace PersistentWorkflows.EntityFrameworkCore.Tests.Execution;

public class DurableWorkflowTests : IAsyncLifetime
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"durable-tests-{Guid.NewGuid():N}.db");
    protected virtual void ConfigureDatabase(DbContextOptionsBuilder options) => options.UseSqlite($"Data Source={_path};Pooling=False");
    private ServiceProvider Provider(Action<PersistentWorkflowsOptions>? policy = null, Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddPersistentWorkflows(policy);
        services.AddPersistentWorkflowsEntityFrameworkCore(ConfigureDatabase);
        services.AddPersistentWorkflowsBackgroundResumer();
        services.AddWorkflow<TestWorkflow, Data>();
        services.AddScoped<ActionStep>();
        services.AddSingleton<Gate>();
        services.AddScoped<AsyncResource>();
        configure?.Invoke(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
    public virtual async ValueTask InitializeAsync()
    {
        await using var provider = Provider();
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PersistentWorkflowsDbContext>().Database.EnsureCreatedAsync();
    }
    public virtual ValueTask DisposeAsync() { File.Delete(_path); return ValueTask.CompletedTask; }
    [Fact]
    public async Task EnqueuedWorkRunsAfterACompleteProviderRestart()
    {
        Guid id;
        await using (var first = Provider())
        {
            await using var scope = first.CreateAsyncScope();
            var accepted = await scope.ServiceProvider.GetRequiredService<IWorkflowRunner>().EnqueueAsync<TestWorkflow, Data>("restart", new());
            Assert.Equal(WorkflowStatus.Pending, accepted.Status); id = accepted.WorkflowInstanceId;
        }
        await using var restarted = Provider();
        await using var execution = restarted.CreateAsyncScope();
        await execution.ServiceProvider.GetRequiredService<IWorkflowResumer>().ProcessOnceAsync(default);
        var runner = execution.ServiceProvider.GetRequiredService<IWorkflowRunner>();
        Assert.Equal("Succeeded", (await runner.GetAsync(id))!.Status);
        Assert.Single(await runner.GetHistoryAsync(id));
    }
    [Fact]
    public async Task FreshManualResumeInitializesTheRegistry()
    {
        Guid id;
        await using (var provider = Provider())
        {
            await using var scope = provider.CreateAsyncScope();
            id = (await scope.ServiceProvider.GetRequiredService<IWorkflowRunner>().EnqueueAsync<TestWorkflow, Data>("manual", new())).WorkflowInstanceId;
        }
        await using var fresh = Provider();
        await using var execution = fresh.CreateAsyncScope();
        var runner = execution.ServiceProvider.GetRequiredService<IWorkflowRunner>();
        await runner.ResumeAsync(id);
        Assert.Equal("Succeeded", (await runner.GetAsync(id))!.Status);
    }
    [Fact]
    public async Task ExceptionsAreRetriedByDefaultThenExhaustedAndManuallyRecoverable()
    {
        await using var provider = Provider(o => { o.MaximumAttempts = 2; o.RetryJitterRatio = 0; });
        await using var scope = provider.CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<IWorkflowRunner>();
        var start = await runner.StartAsync<TestWorkflow, Data>("failure", new() { Mode = "throw" });
        Assert.Equal(WorkflowStatus.Waiting, start.Status);
        Assert.NotNull((await runner.GetAsync(start.WorkflowInstanceId))!.NextExecutionAtUtc);
        await runner.ResumeAsync(start.WorkflowInstanceId);
        var failed = (await runner.GetAsync(start.WorkflowInstanceId))!;
        Assert.Equal("Failed", failed.Status); Assert.Equal("RetriesExhausted", failed.LastErrorCode);
        Assert.Equal(2, (await runner.GetHistoryAsync(failed.Id)).Count);
        Assert.True(await runner.RetryAsync(failed.Id, "Operator repaired dependency"));
        Assert.Equal("Pending", (await runner.GetAsync(failed.Id))!.Status);
        Assert.Equal("ManualRetry", (await runner.GetHistoryAsync(failed.Id)).Last().Status);
    }
    [Fact]
    public async Task NullPayloadSignalCanArriveBeforeWaitingAndIsIdempotent()
    {
        await using var provider = Provider();
        await using var scope = provider.CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<IWorkflowRunner>();
        var accepted = await runner.EnqueueAsync<TestWorkflow, Data>("early", new() { Mode = "signal" });
        Assert.True(await runner.SignalAsync(accepted.WorkflowInstanceId, "approval"));
        Assert.False(await runner.SignalAsync(accepted.WorkflowInstanceId, "approval", "{}"));
        await scope.ServiceProvider.GetRequiredService<IWorkflowResumer>().ProcessOnceAsync(default);
        Assert.Equal("Succeeded", (await runner.GetAsync(accepted.WorkflowInstanceId))!.Status);
    }
    [Fact]
    public async Task WaitingSignalSurvivesRestartAndWakesThroughTheWorker()
    {
        Guid id;
        await using (var provider = Provider())
        {
            await using var scope = provider.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<IWorkflowRunner>().StartAsync<TestWorkflow, Data>("late", new() { Mode = "signal" });
            Assert.Equal(WorkflowStatus.Waiting, result.Status); id = result.WorkflowInstanceId;
        }
        await using var fresh = Provider();
        await using var execution = fresh.CreateAsyncScope();
        var runner = execution.ServiceProvider.GetRequiredService<IWorkflowRunner>();
        await runner.SignalAsync(id, "approval", "{\"approved\":true}");
        await execution.ServiceProvider.GetRequiredService<IWorkflowResumer>().ProcessOnceAsync(default);
        Assert.Equal("Succeeded", (await runner.GetAsync(id))!.Status);
        Assert.Equal(2, (await runner.GetHistoryAsync(id)).Count);
    }
    [Fact]
    public async Task DifferentInputWithSameBusinessKeyIsRejected()
    {
        await using var provider = Provider(); await using var scope = provider.CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<IWorkflowRunner>();
        await runner.EnqueueAsync<TestWorkflow, Data>("key", new());
        await Assert.ThrowsAsync<WorkflowInputConflictException>(() => runner.EnqueueAsync<TestWorkflow, Data>("key", new() { Mode = "different" }));
    }
    [Fact]
    public async Task CooperativeTimeoutIsRecordedAndClassifiersCanStopRetrying()
    {
        await using var provider = Provider(o => { o.StepTimeout = TimeSpan.FromMilliseconds(50); o.MaximumAttempts = 1; });
        await using var scope = provider.CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<IWorkflowRunner>();
        var result = await runner.StartAsync<TestWorkflow, Data>("timeout", new() { Mode = "hang" });
        Assert.Equal(WorkflowStatus.Failed, result.Status);
        Assert.Equal("StepTimeout", (await runner.GetHistoryAsync(result.WorkflowInstanceId))[0].ErrorCode);
    }
    [Fact]
    public async Task ShutdownReleasesAcceptedWorkForRecovery()
    {
        await using var provider = Provider(); await using var scope = provider.CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<IWorkflowRunner>();
        var result = await runner.EnqueueAsync<TestWorkflow, Data>("shutdown", new() { Mode = "hang" });
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.ResumeAsync(result.WorkflowInstanceId, stop.Token));
        Assert.Equal("Pending", (await runner.GetAsync(result.WorkflowInstanceId))!.Status);
    }
    [Fact]
    public async Task CancellationStopsPendingWorkWithoutRunningTheAction()
    {
        await using var provider = Provider(); await using var scope = provider.CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<IWorkflowRunner>();
        var result = await runner.EnqueueAsync<TestWorkflow, Data>("cancel", new());
        Assert.True(await runner.CancelAsync(result.WorkflowInstanceId));
        await scope.ServiceProvider.GetRequiredService<IWorkflowResumer>().ProcessOnceAsync(default);
        Assert.Equal("Cancelled", (await runner.GetAsync(result.WorkflowInstanceId))!.Status);
        Assert.Empty(await runner.GetHistoryAsync(result.WorkflowInstanceId));
    }
    [Fact]
    public async Task ChangedDefinitionIsFailedWithoutRunningAnAction()
    {
        await using var provider = Provider(); await using var scope = provider.CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<IWorkflowRunner>();
        var result = await runner.EnqueueAsync<TestWorkflow, Data>("version", new());
        var db = scope.ServiceProvider.GetRequiredService<PersistentWorkflowsDbContext>();
        await db.WorkflowInstances.Where(x => x.Id == result.WorkflowInstanceId).ExecuteUpdateAsync(s => s.SetProperty(x => x.DefinitionHash, "changed"));
        await runner.ResumeAsync(result.WorkflowInstanceId);
        Assert.Equal("DefinitionUnavailable", (await runner.GetAsync(result.WorkflowInstanceId))!.LastErrorCode);
        Assert.Empty(await runner.GetHistoryAsync(result.WorkflowInstanceId));
    }
    [Fact]
    public async Task HostStartupValidatesStepsAndOptions()
    {
        await using var provider = Provider(o => o.MaximumAttempts = 0);
        Assert.Throws<Microsoft.Extensions.Options.OptionsValidationException>(() => provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<PersistentWorkflowsOptions>>().Value);
    }
    [Fact]
    public async Task LongActionRenewsOwnershipAndRejectsACompetingRunner()
    {
        await using var provider = Provider(o => { o.LeaseDuration = TimeSpan.FromSeconds(1); o.LeaseRenewalInterval = TimeSpan.FromMilliseconds(100); });
        await using var scope = provider.CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<IWorkflowRunner>();
        var id = (await runner.EnqueueAsync<TestWorkflow, Data>("renew", new() { Mode = "gate" })).WorkflowInstanceId;
        var gate = provider.GetRequiredService<Gate>();
        var execution = runner.ResumeAsync(id);
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(1300);
        await using var competingScope = provider.CreateAsyncScope();
        await competingScope.ServiceProvider.GetRequiredService<IWorkflowRunner>().ResumeAsync(id);
        Assert.Equal(1, gate.Calls);
        Assert.True((await runner.GetAsync(id))!.LeaseExpiresAtUtc > DateTime.UtcNow);
        gate.Release.TrySetResult();
        await execution;
        Assert.Equal("Succeeded", (await runner.GetAsync(id))!.Status);
    }
    [Fact]
    public async Task OwnershipLossCancelsTheActionAndRejectsItsProgress()
    {
        await using var provider = Provider(o => { o.LeaseDuration = TimeSpan.FromSeconds(1); o.LeaseRenewalInterval = TimeSpan.FromMilliseconds(50); });
        await using var scope = provider.CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<IWorkflowRunner>();
        var id = (await runner.EnqueueAsync<TestWorkflow, Data>("lost", new() { Mode = "gate" })).WorkflowInstanceId;
        var gate = provider.GetRequiredService<Gate>();
        var execution = runner.ResumeAsync(id);
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var db = scope.ServiceProvider.GetRequiredService<PersistentWorkflowsDbContext>();
        var replacement = Guid.NewGuid();
        await db.WorkflowInstances.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.LeaseToken, (Guid?)replacement));
        await execution.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(replacement, (await runner.GetAsync(id))!.LeaseToken);
        Assert.Equal("Started", (await runner.GetHistoryAsync(id))[0].Status);
        Assert.Equal(0, (await runner.GetAsync(id))!.CurrentStepIndex);
    }
    [Fact]
    public async Task CancellationDuringAnActionIsPersistedAndStopsExecution()
    {
        await using var provider = Provider(o => { o.LeaseDuration = TimeSpan.FromSeconds(1); o.LeaseRenewalInterval = TimeSpan.FromMilliseconds(50); });
        await using var scope = provider.CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<IWorkflowRunner>();
        var id = (await runner.EnqueueAsync<TestWorkflow, Data>("running-cancel", new() { Mode = "gate" })).WorkflowInstanceId;
        var gate = provider.GetRequiredService<Gate>();
        var execution = runner.ResumeAsync(id);
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(await runner.CancelAsync(id));
        await execution.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("Cancelled", (await runner.GetAsync(id))!.Status);
        Assert.Equal("Cancelled", (await runner.GetHistoryAsync(id))[0].Status);
    }
    [Fact]
    public async Task RetriesUseTheSameExternalIdempotencyKey()
    {
        await using var provider = Provider(); await using var scope = provider.CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<IWorkflowRunner>();
        var id = (await runner.StartAsync<TestWorkflow, Data>("effects", new() { Mode = "effect" })).WorkflowInstanceId;
        await runner.ResumeAsync(id);
        Assert.Equal("Succeeded", (await runner.GetAsync(id))!.Status);
        Assert.Single(provider.GetRequiredService<Gate>().Effects);
        Assert.Equal(2, (await runner.GetHistoryAsync(id)).Count);
    }
    [Fact]
    public async Task OldAndNewDefinitionVersionsCanRunTogether()
    {
        Guid oldId;
        await using (var original = Provider())
        {
            await using var originalScope = original.CreateAsyncScope();
            oldId = (await originalScope.ServiceProvider.GetRequiredService<IWorkflowRunner>().EnqueueAsync<TestWorkflow, Data>("old", new())).WorkflowInstanceId;
        }
        await using var upgraded = Provider(configure: services =>
        {
            services.AddWorkflow<VersionTwoWorkflow, Data>(); services.AddScoped<VersionTwoAction>();
        });
        await using var scope = upgraded.CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<IWorkflowRunner>();
        var newId = (await runner.EnqueueAsync<VersionTwoWorkflow, Data>("new", new())).WorkflowInstanceId;
        await scope.ServiceProvider.GetRequiredService<IWorkflowResumer>().ProcessOnceAsync(default);
        Assert.Equal(1, (await runner.GetAsync(oldId))!.DefinitionVersion);
        Assert.Equal(2, (await runner.GetAsync(newId))!.DefinitionVersion);
        Assert.Contains("\"executedVersion\":1", (await runner.GetAsync(oldId))!.ContextJson);
        Assert.Contains("\"executedVersion\":2", (await runner.GetAsync(newId))!.ContextJson);
    }
    [Fact]
    public async Task ContextSchemaMigrationIsExplicitAndPersistsWithOutcome()
    {
        Guid id;
        await using (var original = Provider())
        {
            await using var scope = original.CreateAsyncScope();
            id = (await scope.ServiceProvider.GetRequiredService<IWorkflowRunner>().EnqueueAsync<TestWorkflow, Data>("schema", new() { Mode = "signal" })).WorkflowInstanceId;
        }
        await using var upgraded = Provider(configure: services =>
        {
            services.Configure<PersistentWorkflowsOptions>(o => o.Registrations.RemoveAll(x => x.WorkflowType == typeof(TestWorkflow)));
            services.AddWorkflow<SchemaTwoWorkflow, Data>();
            services.AddSingleton<IWorkflowContextMigrator, TestMigrator>();
        });
        await using var execution = upgraded.CreateAsyncScope();
        var runner = execution.ServiceProvider.GetRequiredService<IWorkflowRunner>();
        await runner.ResumeAsync(id);
        Assert.Equal("Waiting", (await runner.GetAsync(id))!.Status);
        await runner.SignalAsync(id, "approval");
        await runner.ResumeAsync(id);
        var state = (await runner.GetAsync(id))!;
        Assert.Equal("Succeeded", state.Status);
        Assert.Equal(2, state.ContextSchemaVersion);
        Assert.Contains("\"migrated\":true", state.ContextJson);
    }
    [Fact]
    public async Task NonRetryableExceptionAndMissingStepAreDetected()
    {
        await using var provider = Provider(o => o.IsRetryableException = _ => false);
        await using var scope = provider.CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<IWorkflowRunner>();
        var result = await runner.StartAsync<TestWorkflow, Data>("non-retry", new() { Mode = "throw" });
        Assert.Equal(WorkflowStatus.Failed, result.Status);
        Assert.Equal("UnhandledException", (await runner.GetHistoryAsync(result.WorkflowInstanceId))[0].ErrorCode);
        var services = new ServiceCollection();
        services.AddPersistentWorkflows(); services.AddPersistentWorkflowsEntityFrameworkCore(ConfigureDatabase);
        services.AddWorkflow<TestWorkflow, Data>();
        await using var invalid = services.BuildServiceProvider();
        var validator = invalid.GetServices<IHostedService>().Single();
        await Assert.ThrowsAsync<InvalidOperationException>(() => validator.StartAsync(default));
    }
    [Fact]
    public async Task InvalidOutcomeFailsWithoutAnOverflowOrStuckWorkflow()
    {
        await using var provider = Provider(); await using var scope = provider.CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<IWorkflowRunner>();
        var result = await runner.StartAsync<TestWorkflow, Data>("invalid", new() { Mode = "invalid" });
        Assert.Equal(WorkflowStatus.Failed, result.Status);
        Assert.Equal("InvalidStepResult", (await runner.GetAsync(result.WorkflowInstanceId))!.LastErrorCode);
    }
    [Fact]
    public async Task DefinitionsCanHaveAsynchronouslyDisposedScopedDependencies()
    {
        await using var provider = Provider(configure: services =>
        {
            services.AddSingleton<DefinitionDisposal>();
            services.AddScoped<AsyncDefinitionDependency>();
            services.AddWorkflow<AsyncDefinition, Data>();
        });
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IWorkflowRunner>().EnqueueAsync<AsyncDefinition, Data>("async-definition", new());
        Assert.Equal(1, provider.GetRequiredService<DefinitionDisposal>().Count);
    }
    [Fact]
    public async Task UncooperativeActionRetainsItsScopeUntilCompletionAndItsLateResultIsDiscarded()
    {
        await using var provider = Provider(o => { o.MaximumAttempts = 1; o.StepTimeout = TimeSpan.FromMilliseconds(100); });
        await using var scope = provider.CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<IWorkflowRunner>();
        var gate = provider.GetRequiredService<Gate>();
        var result = await runner.StartAsync<TestWorkflow, Data>("detached", new() { Mode = "uncooperative" });
        Assert.Equal(WorkflowStatus.Failed, result.Status);
        Assert.False(gate.Disposed.Task.IsCompleted);
        gate.Release.TrySetResult();
        await gate.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("Failed", (await runner.GetAsync(result.WorkflowInstanceId))!.Status);
        Assert.Equal(0, (await runner.GetAsync(result.WorkflowInstanceId))!.CurrentStepIndex);
    }
    public sealed class Gate
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls;
        public HashSet<string> Effects { get; } = [];
        public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    public sealed class AsyncResource(Gate gate) : IAsyncDisposable
    {
        public bool WasDisposed { get; private set; }
        public ValueTask DisposeAsync() { WasDisposed = true; gate.Disposed.TrySetResult(); return ValueTask.CompletedTask; }
    }
    public sealed class DefinitionDisposal { public int Count; }
    public sealed class AsyncDefinitionDependency(DefinitionDisposal disposal) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() { await Task.Yield(); Interlocked.Increment(ref disposal.Count); }
    }
    public sealed class AsyncDefinition(AsyncDefinitionDependency dependency) : IWorkflowDefinition<Data>
    {
        private readonly AsyncDefinitionDependency _dependency = dependency;
        public string Name => "async-definition";
        public void Build(IWorkflowBuilder<Data> builder) => builder.Step<ActionStep>("action");
    }
    public sealed class Data { public string Mode { get; set; } = "success"; public string? IdempotencyKey { get; set; } public int ExecutedVersion { get; set; } public bool Migrated { get; set; } }
    public sealed class VersionTwoWorkflow : IWorkflowDefinition<Data>
    {
        public string Name => "durable-test";
        public int Version => 2;
        public void Build(IWorkflowBuilder<Data> builder) => builder.Step<VersionTwoAction>("action");
    }
    public sealed class VersionTwoAction : IWorkflowStep<Data>
    {
        public Task<StepResult> ExecuteAsync(WorkflowExecutionContext<Data> context, CancellationToken ct)
        { context.Data.ExecutedVersion = 2; return Task.FromResult(StepResult.Success()); }
    }
    public sealed class SchemaTwoWorkflow : IWorkflowDefinition<Data>
    {
        public string Name => "durable-test";
        public int ContextSchemaVersion => 2;
        public void Build(IWorkflowBuilder<Data> builder) => builder.Step<ActionStep>("action");
    }
    public sealed class TestMigrator : IWorkflowContextMigrator
    {
        public string Migrate(string name, int definitionVersion, int from, int to, string json)
        {
            Assert.Equal(1, from); Assert.Equal(2, to);
            var data = System.Text.Json.Nodes.JsonNode.Parse(json)!;
            data["migrated"] = true; return data.ToJsonString();
        }
    }
    public sealed class TestWorkflow : IWorkflowDefinition<Data>
    {
        string IWorkflowDefinition<Data>.Name => "durable-test";
        void IWorkflowDefinition<Data>.Build(IWorkflowBuilder<Data> builder) => builder.Step<ActionStep>("action");
    }
    public sealed class ActionStep(Gate gate, AsyncResource resource) : IWorkflowStep<Data>
    {
        async Task<StepResult> IWorkflowStep<Data>.ExecuteAsync(WorkflowExecutionContext<Data> context, CancellationToken ct)
        {
            context.Data.IdempotencyKey = context.IdempotencyKey;
            context.Data.ExecutedVersion = 1;
            if (context.Data.Mode == "invalid") return StepResult.Wait(TimeSpan.MaxValue);
            if (context.Data.Mode == "uncooperative")
            {
                gate.Entered.TrySetResult(); await gate.Release.Task;
                Assert.False(resource.WasDisposed);
            }
            if (context.Data.Mode == "gate")
            {
                Interlocked.Increment(ref gate.Calls); gate.Entered.TrySetResult();
                await gate.Release.Task.WaitAsync(ct);
            }
            if (context.Data.Mode == "effect")
            {
                gate.Effects.Add(context.IdempotencyKey);
                if (context.Attempt == 1) throw new IOException("Response lost after external effect");
            }
            if (context.Data.Mode == "throw") throw new IOException("Dependency unavailable");
            if (context.Data.Mode == "hang") await Task.Delay(Timeout.Infinite, ct);
            if (context.Data.Mode == "signal" && await context.GetSignalAsync("approval", ct) is null) return StepResult.WaitForSignal("approval");
            return StepResult.Success();
        }
    }
}
