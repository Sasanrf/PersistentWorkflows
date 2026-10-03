namespace PersistentWorkflows.EntityFrameworkCore.Entities;

public sealed class WorkflowSignalEntity
{
    public Guid WorkflowInstanceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? PayloadJson { get; set; }
    public DateTime ReceivedAtUtc { get; set; }
}
