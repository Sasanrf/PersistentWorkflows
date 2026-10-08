using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PersistentWorkflows.Abstractions.Definitions;
using PersistentWorkflows.Abstractions.Execution;
using PersistentWorkflows.Abstractions.Persistence;
using PersistentWorkflows.Abstractions.Results;
using PersistentWorkflows.Core.DependencyInjection;
using PersistentWorkflows.Core.Execution;
using PersistentWorkflows.EntityFrameworkCore.SqlServer;
using Xunit;

namespace PersistentWorkflows.EntityFrameworkCore.Tests.Execution;

public sealed class SqlServerWorkflowIntegrationTests(ITestOutputHelper output) : IAsyncLifetime
{
    private readonly IsolatedSqlServerDatabase _database = new();
    public ValueTask InitializeAsync() => _database.InitializeAsync();
    public ValueTask DisposeAsync() => _database.DisposeAsync();

    private IHost Host(Probe probe, int batch = 16, int concurrency = 4)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Services.AddPersistentWorkflowsSqlServer(_database.ConnectionString,
            configureExecution: o => { o.RetryJitterRatio = 0; o.InitialRetryDelay = TimeSpan.FromMilliseconds(100); },
            configureWorker: o => { o.BatchSize = batch; o.MaximumConcurrency = concurrency; o.PollingInterval = TimeSpan.FromMilliseconds(25); });
        builder.Services.AddWorkflow<Sequence, Data>();
        builder.Services.AddScoped<First>();
        builder.Services.AddScoped<Second>();
        builder.Services.AddSingleton(probe);
        return builder.Build();
    }

    [Fact]
    public async Task SqlFailureDuringHistoryWriteRollsBackTheEntireCheckpointAndAllowsRetry()
    {
        using var host = Host(new());
        await using var scope = host.Services.CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<IWorkflowRunner>();
        var id = (await runner.EnqueueAsync<Sequence, Data>("rollback", new())).WorkflowInstanceId;
        var store = scope.ServiceProvider.GetRequiredService<IWorkflowStore>();
        var now = DateTime.UtcNow;
        var token = Guid.NewGuid();
        var state = (await store.TryClaimAsync(id, token, now, TimeSpan.FromMinutes(1), false, default))!;
        var attempt = await store.BeginStepAsync(id, token, "first", now, default);
        var before = (await store.GetAsync(id, default))!;
        state.ContextJson = "{\"trace\":[\"first\"]}";
        state.CurrentStepIndex = 1;
        state.CurrentStepName = "second";
        attempt.Status = "Succeeded"; attempt.CompletedAtUtc = now;
        await using var db = _database.Open();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER [PersistentWorkflows].[TestRejectCheckpoint]
            ON [PersistentWorkflows].[WorkflowStepExecutions] AFTER UPDATE AS
            BEGIN
                THROW 51000, 'Injected checkpoint failure', 1;
            END
            """);
        try
        {
            var error = await Assert.ThrowsAsync<SqlException>(() => store.CommitAsync(state, token, attempt, now, default));
            Assert.Equal(51000, error.Number);
            var rolledBack = (await store.GetAsync(id, default))!;
            Assert.Equal(before.ContextJson, rolledBack.ContextJson);
            Assert.Equal(before.CurrentStepIndex, rolledBack.CurrentStepIndex);
            Assert.Equal(before.CurrentStepName, rolledBack.CurrentStepName);
            Assert.Equal(before.Version, rolledBack.Version);
            Assert.Equal(before.Status, rolledBack.Status);
            Assert.Equal(before.LeaseToken, rolledBack.LeaseToken);
            Assert.Equal("Started", Assert.Single(await store.GetHistoryAsync(id, 0, 100, default)).Status);
        }
        finally { await db.Database.ExecuteSqlRawAsync("DROP TRIGGER [PersistentWorkflows].[TestRejectCheckpoint]"); }
        Assert.True(await store.CommitAsync(state, token, attempt, now, default));
        var committed = (await store.GetAsync(id, default))!;
        Assert.Equal(1, committed.CurrentStepIndex);
        Assert.Equal(state.ContextJson, committed.ContextJson);
        Assert.Equal("Succeeded", Assert.Single(await store.GetHistoryAsync(id, 0, 100, default)).Status);
    }

    [Fact]
    public async Task RecoveryPreservesSuccessfulCursorAndAbandonsOnlyInterruptedAttempt()
    {
        Guid id;
        using (var original = Host(new()))
        {
            await using var scope = original.Services.CreateAsyncScope();
            var runner = scope.ServiceProvider.GetRequiredService<IWorkflowRunner>();
            id = (await runner.StartAsync<Sequence, Data>("recovery", new() { Mode = "signal" })).WorkflowInstanceId;
            var state = (await runner.GetAsync(id))!;
            Assert.Equal("Waiting", state.Status);
            Assert.Equal(1, state.CurrentStepIndex);
            // Model a process dying after BeginStep, without releasing its ownership.
            var store = scope.ServiceProvider.GetRequiredService<IWorkflowStore>();
            var beforeCrash = DateTime.UtcNow.AddMinutes(-2);
            var token = Guid.NewGuid();
            Assert.NotNull(await store.TryClaimAsync(id, token, beforeCrash, TimeSpan.FromSeconds(1), true, default));
            Assert.Equal(2, (await store.BeginStepAsync(id, token, "second", beforeCrash, default)).Attempt);
        }
        var probe = new Probe();
        using var restarted = Host(probe);
        await using var recovery = restarted.Services.CreateAsyncScope();
        var fresh = recovery.ServiceProvider.GetRequiredService<IWorkflowRunner>();
        Assert.True(await fresh.SignalAsync(id, "approval"));
        await recovery.ServiceProvider.GetRequiredService<IWorkflowResumer>().ProcessOnceAsync(default);
        var final = (await fresh.GetAsync(id))!;
        Assert.Equal("Succeeded", final.Status);
        Assert.Equal(2, final.CurrentStepIndex);
        Assert.Equal(new[] { "first", "second" }, JsonSerializer.Deserialize<Data>(final.ContextJson, JsonOptions)!.Trace);
        var history = await fresh.GetHistoryAsync(id);
        Assert.Single(history.Where(x => x.StepName == "first"));
        var second = history.Where(x => x.StepName == "second").OrderBy(x => x.Attempt).ToArray();
        Assert.Equal(new[] { "Waiting", "Abandoned", "Succeeded" }, second.Select(x => x.Status));
        Assert.Equal(new[] { 1, 2, 3 }, second.Select(x => x.Attempt));
        Assert.Single(probe.Keys);
        Assert.Equal($"{id:N}:second", probe.Keys.Single());
    }

    [Fact]
    public async Task WorkerBatchAndConcurrencyAreBoundedWithIndependentExecutionScopes()
    {
        var probe = new Probe { Block = true };
        using var host = Host(probe, batch: 5, concurrency: 3);
        await using var scope = host.Services.CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<IWorkflowRunner>();
        for (var i = 0; i < 12; i++) await runner.EnqueueAsync<Sequence, Data>($"bounded-{i}", new());
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var processing = scope.ServiceProvider.GetRequiredService<IWorkflowResumer>().ProcessOnceAsync(deadline.Token);
        try
        {
            await probe.ThreeEntered.Task.WaitAsync(deadline.Token);
            Assert.Equal(3, Volatile.Read(ref probe.Active));
            Assert.Equal(3, probe.ScopeIds.Distinct().Count());
        }
        finally { probe.Release.TrySetResult(); await processing; }
        Assert.Equal(3, probe.Peak);
        Assert.Equal(5, probe.ScopeIds.Count);
        Assert.Equal(5, probe.ScopeIds.Distinct().Count());
        Assert.Equal(5, (await runner.ListAsync(PersistentWorkflows.Abstractions.Enums.WorkflowStatus.Succeeded)).Count);
        Assert.Equal(7, (await runner.ListAsync(PersistentWorkflows.Abstractions.Enums.WorkflowStatus.Pending)).Count);
    }

    [Theory]
    [InlineData(120)]
    [InlineData(480)]
    public async Task TwoHostedWorkersDrainMixedBacklogWithoutLosingOrDuplicatingProgress(int count)
    {
        var left = new Probe();
        var right = new Probe();
        using var first = Host(left);
        using var second = Host(right);
        await using var scope = first.Services.CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<IWorkflowRunner>();
        var ids = new List<(Guid Id, string Mode)>();
        var modes = new[] { "success", "retry", "timer", "signal" };
        var enqueue = Stopwatch.StartNew();
        for (var i = 0; i < count; i++)
        {
            var mode = modes[i % modes.Length];
            var id = (await runner.EnqueueAsync<Sequence, Data>($"load-{i}", new() { Mode = mode })).WorkflowInstanceId;
            ids.Add((id, mode));
            if (mode == "signal") Assert.True(await runner.SignalAsync(id, "approval"));
        }
        enqueue.Stop();
        var elapsed = Stopwatch.StartNew();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        try
        {
            await first.StartAsync(deadline.Token);
            await second.StartAsync(deadline.Token);
            while ((await runner.ListAsync(PersistentWorkflows.Abstractions.Enums.WorkflowStatus.Succeeded, take: 1000, cancellationToken: deadline.Token)).Count != count)
                await Task.Delay(25, deadline.Token);
        }
        finally
        {
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await Task.WhenAll(first.StopAsync(stop.Token), second.StopAsync(stop.Token));
        }
        elapsed.Stop();
        foreach (var (id, mode) in ids)
        {
            var state = (await runner.GetAsync(id))!;
            Assert.Equal("Succeeded", state.Status);
            Assert.Equal(2, state.CurrentStepIndex);
            Assert.Null(state.LeaseToken);
            Assert.Equal(new[] { "first", "second" }, JsonSerializer.Deserialize<Data>(state.ContextJson, JsonOptions)!.Trace);
            var history = await runner.GetHistoryAsync(id);
            Assert.Equal(mode is "retry" or "timer" ? 3 : 2, history.Count);
            Assert.Equal(2, history.Count(x => x.Status == "Succeeded"));
            Assert.Single(history.Where(x => x.StepName == "first"));
            Assert.Equal(mode is "retry" or "timer" ? new[] { 1, 2 } : new[] { 1 },
                history.Where(x => x.StepName == "second").Select(x => x.Attempt));
            var keys = left.Keys.Concat(right.Keys).Where(x => x.StartsWith(id.ToString("N"), StringComparison.Ordinal)).ToArray();
            Assert.Equal(mode is "retry" or "timer" ? 2 : 1, keys.Length);
            Assert.All(keys, key => Assert.Equal($"{id:N}:second", key));
        }
        Assert.InRange(left.Peak, 1, 4);
        Assert.InRange(right.Peak, 1, 4);
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<IWorkflowStore>().GetDueAsync(DateTime.UtcNow, 1000, default));
        await using var db = _database.Open();
        var server = await db.Database.SqlQueryRaw<string>("SELECT CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128)) + ' ' + CAST(SERVERPROPERTY('Edition') AS nvarchar(128)) AS [Value]").SingleAsync();
        output.WriteLine($"server: {server}; runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}; logical_processors: {Environment.ProcessorCount}");
        output.WriteLine($"load: instances={count}; attempts={count * 5 / 2}; enqueue_ms={enqueue.Elapsed.TotalMilliseconds:F1}; drain_ms={elapsed.Elapsed.TotalMilliseconds:F1}; workflows_per_second={count / elapsed.Elapsed.TotalSeconds:F2}; peak_actions_per_host={left.Peak},{right.Peak}");
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    public sealed class Data { public string Mode { get; set; } = "success"; public List<string> Trace { get; set; } = []; }
    public sealed class Sequence : IWorkflowDefinition<Data>
    {
        public string Name => "sql-integration";
        public void Build(IWorkflowBuilder<Data> builder) { builder.Step<First>("first"); builder.Step<Second>("second"); }
    }
    public sealed class First : IWorkflowStep<Data>
    {
        public Task<StepResult> ExecuteAsync(WorkflowExecutionContext<Data> context, CancellationToken ct)
        {
            Assert.Empty(context.Data.Trace);
            context.Data.Trace.Add("first");
            return Task.FromResult(StepResult.Success());
        }
    }
    public sealed class Probe
    {
        public bool Block;
        public int Active;
        public int Peak;
        public ConcurrentBag<Guid> ScopeIds { get; } = [];
        public ConcurrentBag<string> Keys { get; } = [];
        public TaskCompletionSource ThreeEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    public sealed class Second(Probe probe) : IWorkflowStep<Data>
    {
        private readonly Guid _scopeId = Guid.NewGuid();
        public async Task<StepResult> ExecuteAsync(WorkflowExecutionContext<Data> context, CancellationToken ct)
        {
            Assert.Equal(new[] { "first" }, context.Data.Trace);
            probe.Keys.Add(context.IdempotencyKey);
            probe.ScopeIds.Add(_scopeId);
            var active = Interlocked.Increment(ref probe.Active);
            lock (probe) probe.Peak = Math.Max(probe.Peak, active);
            if (active == 3) probe.ThreeEntered.TrySetResult();
            try
            {
                if (probe.Block) await probe.Release.Task.WaitAsync(ct);
                await Task.Delay(20, ct);
                if (context.Data.Mode == "retry" && context.Attempt == 1) return StepResult.Retry("transient", TimeSpan.FromMilliseconds(100));
                if (context.Data.Mode == "timer" && context.Attempt == 1) return StepResult.Wait(TimeSpan.FromMilliseconds(100));
                if (context.Data.Mode == "signal" && await context.GetSignalAsync("approval", ct) is null) return StepResult.WaitForSignal("approval");
                context.Data.Trace.Add("second");
                return StepResult.Success();
            }
            finally { Interlocked.Decrement(ref probe.Active); }
        }
    }
}
