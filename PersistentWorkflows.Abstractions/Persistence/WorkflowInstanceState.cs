namespace PersistentWorkflows.Abstractions.Persistence;

public sealed class WorkflowInstanceState
{
    public required Guid Id { get; init; }
    public required string WorkflowName { get; init; }
    public required string InstanceKey { get; init; }
    public required string ContextJson { get; set; }
    public required string Status { get; set; }
    public Enums.WorkflowStatus ExecutionStatus => Enum.Parse<Enums.WorkflowStatus>(Status);
    public required int CurrentStepIndex { get; set; }
    public required DateTime CreatedAtUtc { get; init; }
    public required DateTime UpdatedAtUtc { get; set; }
    public DateTime? NextExecutionAtUtc { get; set; }
    public long Version { get; set; }
    public int DefinitionVersion { get; set; } = 1;
    public int ContextSchemaVersion { get; set; } = 1;
    public string InputHash { get; set; } = string.Empty;
    public string DefinitionHash { get; set; } = string.Empty;
    public string? CurrentStepName { get; set; }
    public Guid? LeaseToken { get; set; }
    public DateTime? LeaseExpiresAtUtc { get; set; }
    public bool CancellationRequested { get; set; }
    public int ConsecutiveFailures { get; set; }
    public string? WaitingSignal { get; set; }
    public string? LastErrorCode { get; set; }
    public string? LastErrorMessage { get; set; }
}
