using PersistentWorkflows.Abstractions.Definitions;

using PersistentWorkflows.Abstractions.Persistence;

namespace PersistentWorkflows.Abstractions.Execution;

/// <summary>Durably accept, execute, inspect, and operate workflows. Resolve from a service scope.</summary>
public interface IWorkflowRunner
{
    /// <summary>Create an instance and attempt inline execution. Existing instances are returned without execution.</summary>
    Task<WorkflowStartResult> StartAsync<TWorkflow, TContext>(
        string instanceKey,
        TContext context,
        CancellationToken cancellationToken = default)
        where TWorkflow : class, IWorkflowDefinition<TContext>
        where TContext : class;

    /// <summary>Manually resume unfinished work, bypassing wait schedules but never an unexpired execution lease.</summary>
    Task ResumeAsync(
        Guid workflowInstanceId,
        CancellationToken cancellationToken = default);

    /// <summary>Persist pending work without executing its actions. Conflicting input for an existing key is rejected.</summary>
    Task<WorkflowStartResult> EnqueueAsync<TWorkflow, TContext>(string instanceKey, TContext context, CancellationToken cancellationToken = default)
        where TWorkflow : class, IWorkflowDefinition<TContext> where TContext : class;
    /// <summary>Read a detached instance snapshot, or null when absent.</summary>
    Task<WorkflowInstanceState?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    /// <summary>Read a bounded page of detached snapshots. Take must be 1–1000.</summary>
    Task<IReadOnlyList<WorkflowInstanceState>> ListAsync(Enums.WorkflowStatus? status = null, int skip = 0, int take = 100, CancellationToken cancellationToken = default);
    /// <summary>Read execution attempts and operator retry events in chronological order.</summary>
    Task<IReadOnlyList<WorkflowStepExecutionState>> GetHistoryAsync(Guid id, int skip = 0, int take = 100, CancellationToken cancellationToken = default);
    /// <summary>Persist cooperative cancellation. Previously applied business effects are not reversed.</summary>
    Task<bool> CancelAsync(Guid id, CancellationToken cancellationToken = default);
    /// <summary>Return a failed instance to pending with a fresh retry budget and an audited intervention reason.</summary>
    Task<bool> RetryAsync(Guid id, string reason, CancellationToken cancellationToken = default);
    /// <summary>Persist a one-shot immutable signal. The first payload wins; duplicates return false.</summary>
    Task<bool> SignalAsync(Guid id, string signalName, string? payloadJson = null, CancellationToken cancellationToken = default);
    /// <summary>Delete old succeeded/cancelled instances, history, and signals. This also forgets business-key deduplication.</summary>
    Task<int> DeleteCompletedAsync(DateTime completedBeforeUtc, int limit = 100, CancellationToken cancellationToken = default);
}
