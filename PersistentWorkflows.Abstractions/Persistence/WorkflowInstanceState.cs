namespace PersistentWorkflows.Abstractions.Persistence;

public sealed class WorkflowInstanceState
{
    public required Guid Id { get; init; }
    public required string WorkflowName { get; init; }
    public required string InstanceKey { get; init; }
    public required string ContextJson { get; set; }
    public required string Status { get; set; }
    public required int CurrentStepIndex { get; set; }
    public required DateTime CreatedAtUtc { get; init; }
    public required DateTime UpdatedAtUtc { get; set; }
    public DateTime? NextExecutionAtUtc { get; set; }
    public long Version { get; set; }
}