using PersistentWorkflows.Abstractions.Execution;
using PersistentWorkflows.Abstractions.Persistence;
using Xunit;

namespace PersistentWorkflows.EntityFrameworkCore.Tests.Execution;

/// <summary>Derive this suite for each provider. Every call must use an independent database session.</summary>
public abstract partial class WorkflowStoreConformanceTests
{
    protected abstract IWorkflowStore CreateStore();
    protected static readonly DateTime Now = new(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);
    protected static WorkflowInstanceState NewInstance(string key = "key") => new()
    {
        Id = Guid.NewGuid(), WorkflowName = "conformance", InstanceKey = key, ContextJson = "{}", Status = "Pending",
        CurrentStepIndex = 0, CurrentStepName = "action", CreatedAtUtc = Now, UpdatedAtUtc = Now, Version = 1
    };
    [Fact]
    public async Task ConcurrentCreationHasOneWinner()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => CreateStore().CreateAsync(NewInstance(), default))));
        Assert.Single(results.Where(x => x.Created));
        Assert.Single(results.Select(x => x.Instance.Id).Distinct());
    }
    [Fact]
    public async Task ConcurrentClaimsHaveOneOwner()
    {
        var instance = (await CreateStore().CreateAsync(NewInstance(), default)).Instance;
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => CreateStore().TryClaimAsync(instance.Id, Guid.NewGuid(), Now, TimeSpan.FromMinutes(1), false, default))));
        Assert.Single(results.Where(x => x is not null));
    }
    [Fact]
    public async Task ExpiredOwnershipCannotRenewOrCommitAndCanBeRecovered()
    {
        var store = CreateStore();
        var original = (await store.CreateAsync(NewInstance(), default)).Instance;
        var token = Guid.NewGuid();
        var claimed = (await store.TryClaimAsync(original.Id, token, Now, TimeSpan.FromSeconds(1), false, default))!;
        var attempt = await store.BeginStepAsync(original.Id, token, "action", Now, default);
        var later = Now.AddSeconds(2);
        Assert.False(await store.RenewLeaseAsync(original.Id, token, later, TimeSpan.FromMinutes(1), default));
        claimed.Status = "Succeeded";
        Assert.False(await store.CommitAsync(claimed, token, attempt, later, default));
        Assert.Contains(original.Id, await store.GetDueAsync(later, 10, default));
        var newToken = Guid.NewGuid();
        var recovered = await store.TryClaimAsync(original.Id, newToken, later, TimeSpan.FromMinutes(1), false, default);
        Assert.NotNull(recovered);
        var newAttempt = await store.BeginStepAsync(original.Id, newToken, "action", later, default);
        Assert.Equal(2, newAttempt.Attempt);
        Assert.Equal("Abandoned", (await store.GetHistoryAsync(original.Id, 0, 10, default))[0].Status);
        Assert.False(await store.ReleaseAsync(original.Id, token, later, default));
    }
    [Fact]
    public async Task ProgressAndHistoryRollbackTogetherWhenHistoryCommitFails()
    {
        var store = CreateStore();
        var id = (await store.CreateAsync(NewInstance(), default)).Instance.Id;
        var token = Guid.NewGuid();
        var claimed = (await store.TryClaimAsync(id, token, Now, TimeSpan.FromMinutes(1), false, default))!;
        var started = await store.BeginStepAsync(id, token, "action", Now, default);
        claimed.Status = "Succeeded"; claimed.CurrentStepIndex = 1; claimed.ContextJson = "{\"changed\":true}";
        var nonexistent = new WorkflowStepExecutionState { Id = Guid.NewGuid(), WorkflowInstanceId = id, StepName = "action", Status = "Succeeded", Attempt = 1, StartedAtUtc = Now };
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CommitAsync(claimed, token, nonexistent, Now, default));
        var persisted = (await store.GetAsync(id, default))!;
        Assert.Equal("Running", persisted.Status);
        Assert.Equal(0, persisted.CurrentStepIndex);
        Assert.Equal("{}", persisted.ContextJson);
        Assert.Equal("Started", (await store.GetHistoryAsync(id, 0, 10, default))[0].Status);
        started.Status = "Succeeded"; started.CompletedAtUtc = Now;
        Assert.True(await store.CommitAsync(claimed, token, started, Now, default));
        Assert.Equal("Succeeded", (await store.GetAsync(id, default))!.Status);
        Assert.Equal("Succeeded", (await store.GetHistoryAsync(id, 0, 10, default))[0].Status);
    }
    [Fact]
    public async Task CancellationCannotBeOverwrittenByInFlightCompletion()
    {
        var store = CreateStore();
        var id = (await store.CreateAsync(NewInstance(), default)).Instance.Id;
        var token = Guid.NewGuid();
        var claimed = (await store.TryClaimAsync(id, token, Now, TimeSpan.FromMinutes(1), false, default))!;
        var started = await store.BeginStepAsync(id, token, "action", Now, default);
        Assert.True(await store.RequestCancellationAsync(id, Now, default));
        Assert.False(await store.RenewLeaseAsync(id, token, Now, TimeSpan.FromMinutes(1), default));
        claimed.Status = "Succeeded"; started.Status = "Succeeded";
        Assert.True(await store.CommitAsync(claimed, token, started, Now, default));
        Assert.Equal("Cancelled", (await store.GetAsync(id, default))!.Status);
        Assert.True((await store.GetAsync(id, default))!.CancellationRequested);
    }
    [Fact]
    public async Task SignalsAreDurableDeduplicatedAndWakeAnIndefiniteWait()
    {
        var store = CreateStore();
        var id = (await store.CreateAsync(NewInstance(), default)).Instance.Id;
        Assert.True(await store.SignalAsync(id, "approval", null, Now, default));
        Assert.False(await store.SignalAsync(id, "approval", "{}", Now, default));
        Assert.NotNull(await CreateStore().GetSignalAsync(id, "approval", default));
        Assert.Null((await store.GetSignalAsync(id, "approval", default))!.PayloadJson);
        var token = Guid.NewGuid();
        var claimed = (await store.TryClaimAsync(id, token, Now, TimeSpan.FromMinutes(1), false, default))!;
        claimed.Status = "Waiting"; claimed.WaitingSignal = "approval";
        Assert.True(await store.CommitAsync(claimed, token, null, Now, default));
        Assert.Contains(id, await store.GetDueAsync(Now, 10, default));
    }
    [Fact]
    public async Task BatchIsBoundedAndFutureWaitIsNotDue()
    {
        var store = CreateStore();
        for (var i = 0; i < 5; i++) await store.CreateAsync(NewInstance(i.ToString()), default);
        Assert.Equal(2, (await store.GetDueAsync(Now, 2, default)).Count);
        var id = (await store.ListAsync(null, 0, 1, default))[0].Id;
        var token = Guid.NewGuid();
        var claimed = (await store.TryClaimAsync(id, token, Now, TimeSpan.FromMinutes(1), false, default))!;
        claimed.Status = "Waiting"; claimed.NextExecutionAtUtc = Now.AddHours(1);
        await store.CommitAsync(claimed, token, null, Now, default);
        Assert.DoesNotContain(id, await store.GetDueAsync(Now, 10, default));
        Assert.Null(await store.TryClaimAsync(id, Guid.NewGuid(), Now, TimeSpan.FromMinutes(1), false, default));
    }
    [Fact]
    public async Task ManualRetryIsAuditedAndRetentionPreservesFailures()
    {
        var store = CreateStore();
        var id = (await store.CreateAsync(NewInstance(), default)).Instance.Id;
        var token = Guid.NewGuid();
        var state = (await store.TryClaimAsync(id, token, Now, TimeSpan.FromMinutes(1), false, default))!;
        state.Status = "Failed"; state.ConsecutiveFailures = 5;
        await store.CommitAsync(state, token, null, Now, default);
        Assert.Equal(0, await store.DeleteCompletedAsync(Now.AddDays(1), 10, default));
        Assert.True(await store.RetryAsync(id, "Dependency repaired by operator", Now, default));
        Assert.False(await store.RetryAsync(id, "Duplicate retry", Now, default));
        Assert.Equal("ManualRetry", (await store.GetHistoryAsync(id, 0, 10, default))[0].Status);
        state = (await store.TryClaimAsync(id, token, Now, TimeSpan.FromMinutes(1), false, default))!;
        Assert.Equal(0, state.ConsecutiveFailures);
        state.Status = "Succeeded";
        await store.CommitAsync(state, token, null, Now, default);
        Assert.Equal(1, await store.DeleteCompletedAsync(Now.AddDays(1), 10, default));
        Assert.Empty(await store.GetHistoryAsync(id, 0, 10, default));
    }
    [Fact]
    public async Task RepeatedOutcomeCommitAcknowledgesSuccessWithoutReapplyingProgress()
    {
        var store = CreateStore();
        var id = (await store.CreateAsync(NewInstance(), default)).Instance.Id;
        var token = Guid.NewGuid();
        var state = (await store.TryClaimAsync(id, token, Now, TimeSpan.FromMinutes(1), false, default))!;
        var attempt = await store.BeginStepAsync(id, token, "action", Now, default);
        state.Status = "Succeeded"; state.CurrentStepIndex = 1;
        attempt.Status = "Succeeded"; attempt.CompletedAtUtc = Now;
        Assert.True(await store.CommitAsync(state, token, attempt, Now, default));
        var version = (await store.GetAsync(id, default))!.Version;
        Assert.True(await store.CommitAsync(state, token, attempt, Now, default));
        Assert.Equal(version, (await store.GetAsync(id, default))!.Version);
        attempt.ErrorMessage = "Conflicting outcome";
        Assert.False(await store.CommitAsync(state, token, attempt, Now, default));
        Assert.Null((await store.GetHistoryAsync(id, 0, 10, default))[0].ErrorMessage);
    }
}
