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

    public ICollection<WorkflowStepExecutionEntity> StepExecutions { get; set; } =
        new List<WorkflowStepExecutionEntity>();
}