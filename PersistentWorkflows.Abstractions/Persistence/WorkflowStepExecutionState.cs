namespace PersistentWorkflows.Abstractions.Persistence;

public sealed class WorkflowStepExecutionState
{
    public required Guid Id { get; init; }
    public required Guid WorkflowInstanceId { get; init; }
    public required string StepName { get; init; }
    public required string Status { get; set; }
    public required int Attempt { get; set; }
    public required DateTime StartedAtUtc { get; init; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
}