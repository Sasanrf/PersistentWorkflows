namespace PersistentWorkflows.EntityFrameworkCore.Entities;

public sealed class WorkflowStepExecutionEntity
{
    public Guid Id { get; set; }

    public Guid WorkflowInstanceId { get; set; }

    public string StepName { get; set; } = null!;

    public string Status { get; set; } = null!;

    public int Attempt { get; set; }

    public DateTime StartedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }

    public WorkflowInstanceEntity WorkflowInstance { get; set; } = null!;
}