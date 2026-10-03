using System.Text.Json;
using PersistentWorkflows.Abstractions.Execution;
using PersistentWorkflows.Abstractions.Persistence;

namespace PersistentWorkflows.Core.Tests.TestDoubles;

internal sealed class FakeWorkflowStore(IWorkflowInstanceRepository repository, IWorkflowStepExecutionRepository history) : IWorkflowStore
{
    private readonly object _gate = new();
    private readonly Dictionary<(Guid, string), string?> _signals = [];
    private FakeWorkflowInstanceRepository Instances => (FakeWorkflowInstanceRepository)repository;
    private FakeWorkflowStepExecutionRepository Executions => (FakeWorkflowStepExecutionRepository)history;
    private static WorkflowInstanceState Copy(WorkflowInstanceState x) => JsonSerializer.Deserialize<WorkflowInstanceState>(JsonSerializer.Serialize(x))!;
    private WorkflowInstanceState? Find(Guid id) => Instances.Instances.SingleOrDefault(x => x.Id == id);
    private bool Owned(WorkflowInstanceState? x, Guid token, DateTime now) => x?.Status == "Running" && x.LeaseToken == token && x.LeaseExpiresAtUtc > now;
    private bool Due(WorkflowInstanceState x, DateTime now) => x.Status == "Pending" ||
        (x.Status == "Running" && (x.LeaseExpiresAtUtc is null || x.LeaseExpiresAtUtc <= now)) ||
        (x.Status == "Waiting" && (x.NextExecutionAtUtc <= now || x.CancellationRequested || (x.WaitingSignal is not null && _signals.ContainsKey((x.Id, x.WaitingSignal)))));
    public Task<(WorkflowInstanceState Instance, bool Created)> CreateAsync(WorkflowInstanceState x, CancellationToken ct)
    {
        lock (_gate)
        {
            var existing = Instances.Instances.SingleOrDefault(i => i.WorkflowName == x.WorkflowName && i.InstanceKey == x.InstanceKey);
            if (existing is not null) return Task.FromResult((Copy(existing), false));
            repository.AddAsync(Copy(x), ct).GetAwaiter().GetResult();
            return Task.FromResult((Copy(x), true));
        }
    }
    public Task<WorkflowInstanceState?> GetAsync(Guid id, CancellationToken ct) { lock (_gate) return Task.FromResult(Find(id) is { } x ? Copy(x) : null); }
    public Task<IReadOnlyList<Guid>> GetDueAsync(DateTime now, int limit, CancellationToken ct)
    { lock (_gate) return Task.FromResult<IReadOnlyList<Guid>>(Instances.Instances.Where(x => Due(x, now)).Take(limit).Select(x => x.Id).ToList()); }
    public Task<WorkflowInstanceState?> TryClaimAsync(Guid id, Guid token, DateTime now, TimeSpan duration, bool force, CancellationToken ct)
    {
        lock (_gate)
        {
            var x = Find(id);
            if (x is null || !(Due(x, now) || (force && x.Status == "Waiting"))) return Task.FromResult<WorkflowInstanceState?>(null);
            x.Status = "Running"; x.LeaseToken = token; x.LeaseExpiresAtUtc = now.Add(duration); x.Version++;
            return Task.FromResult<WorkflowInstanceState?>(Copy(x));
        }
    }
    public Task<bool> RenewLeaseAsync(Guid id, Guid token, DateTime now, TimeSpan duration, CancellationToken ct)
    { lock (_gate) { var x = Find(id); if (!Owned(x, token, now) || x!.CancellationRequested) return Task.FromResult(false); x.LeaseExpiresAtUtc = now.Add(duration); return Task.FromResult(true); } }
    public Task<WorkflowStepExecutionState> BeginStepAsync(Guid id, Guid token, string name, DateTime now, CancellationToken ct)
    {
        lock (_gate)
        {
            if (!Owned(Find(id), token, now)) throw new WorkflowLeaseLostException();
            var x = new WorkflowStepExecutionState { Id = Guid.NewGuid(), WorkflowInstanceId = id, StepName = name, Attempt = history.GetNextAttemptAsync(id, name, ct).Result, Status = "Started", StartedAtUtc = now };
            history.AddAsync(x, ct).GetAwaiter().GetResult(); return Task.FromResult(x);
        }
    }
    public Task<bool> CommitAsync(WorkflowInstanceState state, Guid token, WorkflowStepExecutionState? execution, DateTime now, CancellationToken ct)
    {
        lock (_gate)
        {
            var x = Find(state.Id);
            if (!Owned(x, token, now)) return Task.FromResult(false);
            var cancel = x!.CancellationRequested;
            foreach (var p in typeof(WorkflowInstanceState).GetProperties().Where(p => p.CanWrite)) p.SetValue(x, p.GetValue(state));
            x.UpdatedAtUtc = now; x.CancellationRequested = cancel; x.Version++;
            if (cancel) x.Status = "Cancelled";
            if (x.Status != "Running") { x.LeaseToken = null; x.LeaseExpiresAtUtc = null; }
            if (execution is not null) history.UpdateAsync(execution, ct).GetAwaiter().GetResult();
            return Task.FromResult(true);
        }
    }
    public Task<bool> ReleaseAsync(Guid id, Guid token, DateTime now, CancellationToken ct)
    { lock (_gate) { var x = Find(id); if (!Owned(x, token, now)) return Task.FromResult(false); x!.Status = x.CancellationRequested ? "Cancelled" : "Pending"; x.LeaseToken = null; x.LeaseExpiresAtUtc = null; return Task.FromResult(true); } }
    public Task<bool> RequestCancellationAsync(Guid id, DateTime now, CancellationToken ct)
    { lock (_gate) { var x = Find(id); if (x is null || x.Status is "Succeeded" or "Failed" or "Cancelled") return Task.FromResult(false); x.CancellationRequested = true; if (x.Status != "Running") x.Status = "Cancelled"; return Task.FromResult(true); } }
    public Task<bool> RetryAsync(Guid id, string reason, DateTime now, CancellationToken ct)
    { lock (_gate) { var x = Find(id); if (x?.Status != "Failed") return Task.FromResult(false); x.Status = "Pending"; x.ConsecutiveFailures = 0; return Task.FromResult(true); } }
    public Task<bool> SignalAsync(Guid id, string name, string? payload, DateTime now, CancellationToken ct)
    { lock (_gate) return Task.FromResult(_signals.TryAdd((id, name), payload)); }
    public Task<WorkflowSignalState?> GetSignalAsync(Guid id, string name, CancellationToken ct)
    { lock (_gate) return Task.FromResult(_signals.TryGetValue((id, name), out var payload) ? new WorkflowSignalState(name, payload, DateTime.UtcNow) : null); }
    public Task<IReadOnlyList<WorkflowInstanceState>> ListAsync(string? status, int skip, int take, CancellationToken ct)
    { lock (_gate) return Task.FromResult<IReadOnlyList<WorkflowInstanceState>>(Instances.Instances.Where(x => status is null || x.Status == status).Skip(skip).Take(take).Select(Copy).ToList()); }
    public Task<IReadOnlyList<WorkflowStepExecutionState>> GetHistoryAsync(Guid id, int skip, int take, CancellationToken ct)
    { lock (_gate) return Task.FromResult<IReadOnlyList<WorkflowStepExecutionState>>(Executions.Executions.Where(x => x.WorkflowInstanceId == id).Skip(skip).Take(take).ToList()); }
    public Task<int> DeleteCompletedAsync(DateTime before, int limit, CancellationToken ct) => throw new NotSupportedException();
}
