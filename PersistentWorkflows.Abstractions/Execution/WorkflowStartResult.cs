namespace PersistentWorkflows.Abstractions.Execution;

public sealed class WorkflowStartResult
{
    public Guid WorkflowInstanceId { get; init; }
    public bool AlreadyExists { get; init; }
    public bool WasCreated => !AlreadyExists;
    public Enums.WorkflowStatus Status { get; init; }
}
