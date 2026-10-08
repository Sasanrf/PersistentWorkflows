using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using FailureWorker;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PersistentWorkflows.Abstractions.Execution;
using PersistentWorkflows.Abstractions.Persistence;
using PersistentWorkflows.EntityFrameworkCore.Persistence;
using Xunit;

namespace PersistentWorkflows.EntityFrameworkCore.Tests.Execution;

public sealed class SqlServerFailureTests(ITestOutputHelper output) : IAsyncLifetime
{
    private readonly IsolatedSqlServerDatabase _database = new();
    public ValueTask InitializeAsync() => _database.InitializeAsync();
    public ValueTask DisposeAsync() => _database.DisposeAsync();
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private IHost Host(StepControl? control = null)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        // Fast failures keep the outage test bounded; no retrying/pooling of failed logins.
        var connection = new SqlConnectionStringBuilder(_database.ConnectionString)
        { ConnectTimeout = 1, ConnectRetryCount = 0, Pooling = false }.ConnectionString;
        WorkerSetup.Add(builder.Services, connection, control ?? new());
        return builder.Build();
    }

    [Fact]
    public async Task KilledWorkerIsRecoveredByAnotherProcessWithoutReplayingTheCommittedStep()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = deadline.Token;
        using var host = Host();
        await using var scope = host.Services.CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<IWorkflowRunner>();
        var id = (await runner.EnqueueAsync<Sequence, Payload>("kill-worker", new(), ct)).WorkflowInstanceId;
        int killedPid;
        await using (var child = StartWorker("block"))
        {
            killedPid = child.Process.Id;
            Assert.Equal($"SECOND {id:N}:second 1", await child.Process.StandardOutput.ReadLineAsync(ct));
            var before = (await runner.GetAsync(id, ct))!;
            Assert.Equal("Running", before.Status);
            Assert.Equal(1, before.CurrentStepIndex);
            Assert.True(before.LeaseExpiresAtUtc > DateTime.UtcNow);
            Assert.Equal(new[] { "Succeeded", "Started" }, (await runner.GetHistoryAsync(id, cancellationToken: ct)).Select(x => x.Status));
            // Abrupt OS termination: no host shutdown and no opportunity to ReleaseAsync.
            child.Process.Kill(entireProcessTree: true);
            await child.Process.WaitForExitAsync(ct);
            Assert.NotEqual(0, child.Process.ExitCode);
            var abandoned = (await runner.GetAsync(id, ct))!;
            Assert.Equal("Running", abandoned.Status);
            Assert.Equal(before.LeaseToken, abandoned.LeaseToken);
        }
        var watch = Stopwatch.StartNew();
        await using var replacement = StartWorker("recover");
        Assert.NotEqual(killedPid, replacement.Process.Id);
        Assert.Equal($"SECOND {id:N}:second 2", await replacement.Process.StandardOutput.ReadLineAsync(ct));
        await WaitForSuccess(runner, id, ct);
        await AssertRecovered(runner, id, ct);
        output.WriteLine($"process-kill: original_pid={killedPid}; replacement_pid={replacement.Process.Id}; recovery_ms={watch.Elapsed.TotalMilliseconds:F1}");
    }

    [Fact]
    public async Task DatabaseOutageCancelsTheActiveAttemptAndTheSameHostRecoversAfterReconnect()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = deadline.Token;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var keys = new ConcurrentQueue<string>();
        using var host = Host(new StepControl
        {
            BeforeSecond = async (context, token) =>
            {
                keys.Enqueue(context.IdempotencyKey);
                if (context.Attempt != 1) return;
                context.Data.Trace.Add("uncommitted-mutation");
                entered.TrySetResult();
                try { await Task.Delay(Timeout.Infinite, token); }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                { cancelled.TrySetResult(); throw; }
            }
        });
        await using var scope = host.Services.CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<IWorkflowRunner>();
        var id = (await runner.EnqueueAsync<Sequence, Payload>("offline-database", new(), ct)).WorkflowInstanceId;
        await host.StartAsync(ct);
        try
        {
            await entered.Task.WaitAsync(ct);
            try
            {
                await _database.SetOnlineAsync(false);
                var unavailable = new SqlConnectionStringBuilder(_database.ConnectionString)
                { ConnectTimeout = 1, ConnectRetryCount = 0, Pooling = false }.ConnectionString;
                await using var probe = new SqlConnection(unavailable);
                await Assert.ThrowsAsync<SqlException>(() => probe.OpenAsync(ct));
                // No caller cancellation or step timeout: failed lease renewal must interrupt the action.
                await cancelled.Task.WaitAsync(ct);
            }
            finally { await _database.SetOnlineAsync(true); }
            var watch = Stopwatch.StartNew();
            await WaitForSuccess(runner, id, ct);
            await AssertRecovered(runner, id, ct);
            Assert.Equal(new[] { $"{id:N}:second", $"{id:N}:second" }, keys);
            output.WriteLine($"database-outage: active_action_cancelled=true; same_host_recovered=true; recovery_after_online_ms={watch.Elapsed.TotalMilliseconds:F1}");
        }
        finally
        {
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await host.StopAsync(stop.Token);
        }
    }

    [Fact]
    public async Task LostBeginAcknowledgementRetriesWithTheSameAttemptIdentity()
    {
        var observer = Store();
        var (id, token, state) = await Claimed(observer);
        Guid committedId = default;
        var loss = new LoseAcknowledgement(async () =>
        {
            var durable = Assert.Single(await observer.GetHistoryAsync(id, 0, 100, default));
            committedId = durable.Id;
            Assert.Equal("Started", durable.Status);
            Assert.Equal(1, durable.Attempt);
        });
        var watch = Stopwatch.StartNew();
        var attempt = await Store(loss).BeginStepAsync(id, token, "first", DateTime.UtcNow, default);
        Assert.Equal(1, loss.Faults);
        Assert.Equal(2, loss.CommittedCallbacks); // Original SQL commit and execution-strategy retry.
        Assert.Equal(committedId, attempt.Id);
        Assert.Equal(1, attempt.Attempt);
        Assert.Equal("Started", attempt.Status);
        Assert.Equal(committedId, Assert.Single(await observer.GetHistoryAsync(id, 0, 100, default)).Id);
        Assert.Equal(state.ContextJson, (await observer.GetAsync(id, default))!.ContextJson);
        output.WriteLine($"lost-begin-ack: faults={loss.Faults}; commit_callbacks={loss.CommittedCallbacks}; elapsed_ms={watch.Elapsed.TotalMilliseconds:F1}");
    }

    [Theory]
    [InlineData("Running")]
    [InlineData("Succeeded")]
    public async Task LostOutcomeAcknowledgementRetriesWithoutApplyingProgressTwice(string status)
    {
        var observer = Store();
        var (id, token, state) = await Claimed(observer);
        var now = DateTime.UtcNow;
        var attempt = await observer.BeginStepAsync(id, token, "first", now, default);
        var before = (await observer.GetAsync(id, default))!;
        state.ContextJson = "{\"trace\":[\"first\"]}";
        state.CurrentStepIndex = 1; state.Status = status;
        attempt.Status = "Succeeded"; attempt.CompletedAtUtc = now;
        var loss = new LoseAcknowledgement(async () =>
        {
            var durable = (await observer.GetAsync(id, default))!;
            Assert.Equal(status, durable.Status);
            Assert.Equal(before.Version + 1, durable.Version);
            Assert.Equal(state.ContextJson, durable.ContextJson);
            Assert.Equal("Succeeded", Assert.Single(await observer.GetHistoryAsync(id, 0, 100, default)).Status);
        });
        var watch = Stopwatch.StartNew();
        // Timeout is raised after SQL COMMIT, inside the provider execution strategy's delegate.
        Assert.True(await Store(loss).CommitAsync(state, token, attempt, now, default));
        Assert.Equal(1, loss.Faults);
        Assert.Equal(1, loss.CommittedCallbacks); // Retry acknowledges existing outcome without another commit.
        var final = (await observer.GetAsync(id, default))!;
        Assert.Equal(before.Version + 1, final.Version);
        Assert.Equal(1, final.CurrentStepIndex);
        Assert.Equal(status, final.Status);
        Assert.Equal(state.ContextJson, final.ContextJson);
        Assert.Equal(status == "Running" ? token : (Guid?)null, final.LeaseToken);
        Assert.Equal(attempt.Id, Assert.Single(await observer.GetHistoryAsync(id, 0, 100, default)).Id);
        attempt.ErrorMessage = "Conflicting repeated result";
        Assert.False(await observer.CommitAsync(state, token, attempt, now, default));
        Assert.Null(Assert.Single(await observer.GetHistoryAsync(id, 0, 100, default)).ErrorMessage);
        output.WriteLine($"lost-outcome-ack: status={status}; faults={loss.Faults}; version_delta={final.Version - before.Version}; elapsed_ms={watch.Elapsed.TotalMilliseconds:F1}");
    }

    private IWorkflowStore Store(LoseAcknowledgement? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<PersistentWorkflowsDbContext>();
        _database.Configure(options);
        if (interceptor is not null)
        {
            // Opt in explicitly: the packaged registration does not enable transient retries by default.
            options.UseSqlServer(_database.ConnectionString, sql => sql.EnableRetryOnFailure(2, TimeSpan.Zero, null));
            options.AddInterceptors(interceptor);
        }
        return new EfWorkflowStore(options.Options);
    }

    private async Task<(Guid Id, Guid Token, WorkflowInstanceState State)> Claimed(IWorkflowStore store)
    {
        using var host = Host();
        await using var scope = host.Services.CreateAsyncScope();
        var id = (await scope.ServiceProvider.GetRequiredService<IWorkflowRunner>().EnqueueAsync<Sequence, Payload>("ack-loss", new())).WorkflowInstanceId;
        var token = Guid.NewGuid();
        var state = await store.TryClaimAsync(id, token, DateTime.UtcNow, TimeSpan.FromMinutes(1), false, default);
        Assert.NotNull(state);
        return (id, token, state);
    }

    private static async Task WaitForSuccess(IWorkflowRunner runner, Guid id, CancellationToken ct)
    {
        while ((await runner.GetAsync(id, ct))!.Status != "Succeeded") await Task.Delay(50, ct);
    }

    private static async Task AssertRecovered(IWorkflowRunner runner, Guid id, CancellationToken ct)
    {
        var final = (await runner.GetAsync(id, ct))!;
        Assert.Equal("Succeeded", final.Status);
        Assert.Equal(2, final.CurrentStepIndex);
        Assert.Null(final.LeaseToken);
        Assert.Equal(new[] { "first", "second" }, JsonSerializer.Deserialize<Payload>(final.ContextJson, JsonOptions)!.Trace);
        var history = await runner.GetHistoryAsync(id, cancellationToken: ct);
        Assert.Equal(3, history.Count);
        Assert.Equal("Succeeded", Assert.Single(history.Where(x => x.StepName == "first")).Status);
        var retried = history.Where(x => x.StepName == "second").OrderBy(x => x.Attempt).ToArray();
        Assert.Equal(new[] { 1, 2 }, retried.Select(x => x.Attempt));
        Assert.Equal(new[] { "Abandoned", "Succeeded" }, retried.Select(x => x.Status));
        Assert.Equal("ExecutionInterrupted", retried[0].ErrorCode);
    }

    private ChildWorker StartWorker(string mode)
    {
        var dll = Path.Combine(AppContext.BaseDirectory, "failure-worker", "FailureWorker.dll");
        Assert.True(File.Exists(dll), $"Build the test project first; missing {dll}");
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(dll);
        start.ArgumentList.Add(mode);
        start.Environment["PERSISTENTWORKFLOWS_FAILURE_WORKER_CONNECTION"] = _database.ConnectionString;
        return new ChildWorker(Process.Start(start) ?? throw new InvalidOperationException("Child worker failed to start."));
    }

    private sealed class ChildWorker(Process process) : IAsyncDisposable
    {
        public Process Process { get; } = process;
        private readonly Task<string> _stderr = process.StandardError.ReadToEndAsync();
        public async ValueTask DisposeAsync()
        {
            if (!Process.HasExited) Process.Kill(entireProcessTree: true);
            await Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            var error = await _stderr;
            Process.Dispose();
            Assert.True(string.IsNullOrWhiteSpace(error), error);
        }
    }

    private sealed class LoseAcknowledgement(Func<Task> verifyDurableCommit) : DbTransactionInterceptor
    {
        public int Faults { get; private set; }
        public int CommittedCallbacks { get; private set; }
        public override async Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            CommittedCallbacks++;
            if (Faults != 0) return;
            await verifyDurableCommit();
            Faults++;
            throw new TimeoutException("Injected lost acknowledgement after independently verified SQL COMMIT.");
        }
    }
}
