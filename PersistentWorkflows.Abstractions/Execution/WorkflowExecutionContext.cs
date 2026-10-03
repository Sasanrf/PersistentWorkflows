namespace PersistentWorkflows.Abstractions.Execution;

public sealed class WorkflowExecutionContext<TContext>
    where TContext : class
{
    public required Guid WorkflowInstanceId { get; init; }
    public required string WorkflowName { get; init; }
    public required string InstanceKey { get; init; }
    public required string StepName { get; init; }
    public required int Attempt { get; init; }
    public required TContext Data { get; init; }
    public required IServiceProvider Services { get; init; }
    /// <summary>Stable across attempts. Use this for deduplicating external effects.</summary>
    public string IdempotencyKey => $"{WorkflowInstanceId:N}:{StepName}";
    public string? SignalPayloadJson { get; init; }
    /// <summary>Signals are durable, immutable, and available even when they arrive before the wait.</summary>
    public Task<Persistence.WorkflowSignalState?> GetSignalAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var store = Services.GetService(typeof(Persistence.IWorkflowStore)) as Persistence.IWorkflowStore
            ?? throw new InvalidOperationException("No workflow store is registered.");
        return store.GetSignalAsync(WorkflowInstanceId, name, cancellationToken);
    }
}
