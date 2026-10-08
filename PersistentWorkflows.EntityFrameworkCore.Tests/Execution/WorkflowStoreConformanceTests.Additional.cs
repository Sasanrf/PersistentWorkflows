using PersistentWorkflows.Abstractions.Execution;
using Xunit;

namespace PersistentWorkflows.EntityFrameworkCore.Tests.Execution;

public abstract partial class WorkflowStoreConformanceTests
{
    [Fact]
    public async Task ConcurrentSignalsKeepExactlyTheWinningPayload()
    {
        var id = (await CreateStore().CreateAsync(NewInstance(), default)).Instance.Id;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var deliveries = Enumerable.Range(0, 16).Select(async i =>
        {
            await start.Task;
            var payload = $"{{\"delivery\":{i}}}";
            return (Payload: payload, Won: await CreateStore().SignalAsync(id, "approval", payload, Now, default));
        }).ToArray();
        start.SetResult();
        var winner = Assert.Single((await Task.WhenAll(deliveries)).Where(x => x.Won));
        Assert.Equal(winner.Payload, (await CreateStore().GetSignalAsync(id, "approval", default))!.PayloadJson);
        Assert.False(await CreateStore().SignalAsync(id, "approval", "replacement", Now, default));
        Assert.Equal(winner.Payload, (await CreateStore().GetSignalAsync(id, "approval", default))!.PayloadJson);
    }

    [Fact]
    public async Task ConcurrentManualRetriesRecordOnlyTheWinningIntervention()
    {
        var store = CreateStore();
        var id = (await store.CreateAsync(NewInstance(), default)).Instance.Id;
        var token = Guid.NewGuid();
        var state = (await store.TryClaimAsync(id, token, Now, TimeSpan.FromMinutes(1), false, default))!;
        state.Status = "Failed"; state.CurrentStepIndex = 2; state.ContextJson = "{\"saved\":true}"; state.ConsecutiveFailures = 5;
        Assert.True(await store.CommitAsync(state, token, null, Now, default));
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var retries = Enumerable.Range(0, 8).Select(async i =>
        {
            await start.Task;
            var reason = $"Operator {i} repaired dependency";
            return (Reason: reason, Won: await CreateStore().RetryAsync(id, reason, Now, default));
        }).ToArray();
        start.SetResult();
        var winner = Assert.Single((await Task.WhenAll(retries)).Where(x => x.Won));
        var audit = Assert.Single(await store.GetHistoryAsync(id, 0, 100, default));
        Assert.Equal("ManualRetry", audit.Status);
        Assert.Equal(winner.Reason, audit.ErrorMessage);
        var persisted = (await store.GetAsync(id, default))!;
        Assert.Equal("Pending", persisted.Status);
        Assert.Equal(0, persisted.ConsecutiveFailures);
        Assert.Equal(2, persisted.CurrentStepIndex);
        Assert.Equal(state.ContextJson, persisted.ContextJson);
    }

    [Theory]
    [InlineData("Succeeded")]
    [InlineData("Cancelled")]
    [InlineData("Failed")]
    public async Task ForceBypassesWaitButCannotStealLiveOwnershipOrRestartTerminalState(string terminal)
    {
        var store = CreateStore();
        var id = (await store.CreateAsync(NewInstance(), default)).Instance.Id;
        var token = Guid.NewGuid();
        var state = (await store.TryClaimAsync(id, token, Now, TimeSpan.FromMinutes(1), false, default))!;
        Assert.Null(await CreateStore().TryClaimAsync(id, Guid.NewGuid(), Now, TimeSpan.FromMinutes(1), true, default));
        await Assert.ThrowsAsync<WorkflowLeaseLostException>(() => CreateStore().BeginStepAsync(id, Guid.NewGuid(), "action", Now, default));
        state.Status = "Waiting"; state.NextExecutionAtUtc = Now.AddDays(1);
        Assert.True(await store.CommitAsync(state, token, null, Now, default));
        Assert.Null(await store.TryClaimAsync(id, token, Now, TimeSpan.FromMinutes(1), false, default));
        state = (await store.TryClaimAsync(id, token, Now, TimeSpan.FromMinutes(1), true, default))!;
        Assert.NotNull(state);
        state.Status = terminal;
        Assert.True(await store.CommitAsync(state, token, null, Now, default));
        Assert.Null(await store.TryClaimAsync(id, token, Now, TimeSpan.FromMinutes(1), true, default));
        Assert.False(await store.ReleaseAsync(id, token, Now, default));
        Assert.False(await store.RequestCancellationAsync(id, Now, default));
        Assert.Empty(await store.GetHistoryAsync(id, 0, 100, default));
    }

    [Fact]
    public async Task PagesAreDetachedAndCleanupIsBoundedCascadingAndForgetsKeys()
    {
        var store = CreateStore();
        for (var i = 0; i < 7; i++)
        {
            var id = (await store.CreateAsync(NewInstance($"page-{i}"), default)).Instance.Id;
            await store.SignalAsync(id, "approval", "{}", Now, default);
            var token = Guid.NewGuid();
            var state = (await store.TryClaimAsync(id, token, Now, TimeSpan.FromMinutes(1), false, default))!;
            var attempt = await store.BeginStepAsync(id, token, "action", Now, default);
            state.Status = i == 6 ? "Failed" : i % 2 == 0 ? "Succeeded" : "Cancelled";
            attempt.Status = state.Status; attempt.CompletedAtUtc = Now;
            Assert.True(await store.CommitAsync(state, token, attempt, Now, default));
        }
        var whole = await store.ListAsync(null, 0, 100, default);
        var pages = new List<Guid>();
        for (var skip = 0; skip < 7; skip += 2)
            pages.AddRange((await CreateStore().ListAsync(null, skip, 2, default)).Select(x => x.Id));
        Assert.Equal(whole.Select(x => x.Id), pages);
        Assert.Equal(7, pages.Distinct().Count());
        whole[0].ContextJson = "mutated snapshot";
        Assert.Equal("{}", (await store.GetAsync(whole[0].Id, default))!.ContextJson);
        Assert.Equal(2, await store.DeleteCompletedAsync(Now.AddDays(1), 2, default));
        var remaining = await store.ListAsync(null, 0, 100, default);
        Assert.Equal(5, remaining.Count);
        Assert.Single(remaining.Where(x => x.Status == "Failed"));
        var deleted = whole.Where(x => remaining.All(y => y.Id != x.Id)).ToArray();
        Assert.Equal(2, deleted.Length);
        foreach (var instance in deleted)
        {
            Assert.Empty(await store.GetHistoryAsync(instance.Id, 0, 100, default));
            Assert.Null(await store.GetSignalAsync(instance.Id, "approval", default));
            var replacement = await store.CreateAsync(NewInstance(instance.InstanceKey), default);
            Assert.True(replacement.Created);
            Assert.NotEqual(instance.Id, replacement.Instance.Id);
        }
        Assert.Equal(4, await store.DeleteCompletedAsync(Now.AddDays(1), 100, default));
        Assert.Equal(0, await store.DeleteCompletedAsync(Now.AddDays(1), 100, default));
    }
}
