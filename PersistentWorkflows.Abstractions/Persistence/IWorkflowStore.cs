namespace PersistentWorkflows.Abstractions.Persistence;

/// <summary>Atomic provider operations. Implementations must pass the provider conformance tests.
/// Ownership mutations must fence by token and unexpired lease; progress and history commit together.</summary>
public interface IWorkflowStore
{
    /// <summary>Atomically create by workflow name/business key or return the winning existing instance.</summary>
    Task<(WorkflowInstanceState Instance, bool Created)> CreateAsync(WorkflowInstanceState instance, CancellationToken cancellationToken);
    /// <summary>Read detached durable state.</summary>
    Task<WorkflowInstanceState?> GetAsync(Guid id, CancellationToken cancellationToken);
    /// <summary>Find a storage-bounded batch including pending, scheduled/signaled, and abandoned work.</summary>
    Task<IReadOnlyList<Guid>> GetDueAsync(DateTime now, int limit, CancellationToken cancellationToken);
    /// <summary>Claim eligible work atomically. Force bypasses waits, never a live owner or terminal state.</summary>
    Task<WorkflowInstanceState?> TryClaimAsync(Guid id, Guid token, DateTime now, TimeSpan leaseDuration, bool force, CancellationToken cancellationToken);
    /// <summary>Extend an unexpired matching lease unless cancellation was requested.</summary>
    Task<bool> RenewLeaseAsync(Guid id, Guid token, DateTime now, TimeSpan leaseDuration, CancellationToken cancellationToken);
    /// <summary>Fence ownership and allocate a durable attempt, abandoning interrupted starts.</summary>
    Task<WorkflowStepExecutionState> BeginStepAsync(Guid id, Guid token, string stepName, DateTime now, CancellationToken cancellationToken);
    /// <summary>Atomically commit context, cursor, outcome, and optional attempt history without overwriting cancellation.</summary>
    Task<bool> CommitAsync(WorkflowInstanceState instance, Guid token, WorkflowStepExecutionState? execution, DateTime now, CancellationToken cancellationToken);
    /// <summary>Release only the unexpired matching owner, returning work to pending or cancelled.</summary>
    Task<bool> ReleaseAsync(Guid id, Guid token, DateTime now, CancellationToken cancellationToken);
    /// <summary>Persist cooperative cancellation for unfinished work.</summary>
    Task<bool> RequestCancellationAsync(Guid id, DateTime now, CancellationToken cancellationToken);
    /// <summary>Atomically reopen a failed instance and audit the intervention reason.</summary>
    Task<bool> RetryAsync(Guid id, string reason, DateTime now, CancellationToken cancellationToken);
    /// <summary>Insert an immutable one-shot signal for an existing instance; duplicates return false.</summary>
    Task<bool> SignalAsync(Guid id, string signalName, string? payloadJson, DateTime now, CancellationToken cancellationToken);
    /// <summary>Read a signal receipt; a null payload is distinct from an absent receipt.</summary>
    Task<WorkflowSignalState?> GetSignalAsync(Guid id, string signalName, CancellationToken cancellationToken);
    /// <summary>Read deterministic bounded pages of detached instances.</summary>
    Task<IReadOnlyList<WorkflowInstanceState>> ListAsync(string? status, int skip, int take, CancellationToken cancellationToken);
    /// <summary>Read deterministic bounded pages of history.</summary>
    Task<IReadOnlyList<WorkflowStepExecutionState>> GetHistoryAsync(Guid id, int skip, int take, CancellationToken cancellationToken);
    /// <summary>Delete bounded old succeeded/cancelled instances and dependent records; preserve failures.</summary>
    Task<int> DeleteCompletedAsync(DateTime before, int limit, CancellationToken cancellationToken);
}
