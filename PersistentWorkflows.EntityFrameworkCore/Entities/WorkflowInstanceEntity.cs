namespace PersistentWorkflows.EntityFrameworkCore.Entities;

public sealed class WorkflowInstanceEntity
{
    public Guid Id { get; set; }

    public string WorkflowName { get; set; } = null!;

    public string InstanceKey { get; set; } = null!;

    public string ContextJson { get; set; } = null!;

    public string Status { get; set; } = null!;

    public int CurrentStepIndex { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

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

    public ICollection<WorkflowStepExecutionEntity> StepExecutions { get; set; } =
        new List<WorkflowStepExecutionEntity>();
}
