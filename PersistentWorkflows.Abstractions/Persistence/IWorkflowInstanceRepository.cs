namespace PersistentWorkflows.Abstractions.Persistence;

public interface IWorkflowInstanceRepository
{
    Task<WorkflowInstanceState?> GetByWorkflowNameAndInstanceKeyAsync(
        string workflowName,
        string instanceKey,
        CancellationToken cancellationToken);

    Task<WorkflowInstanceState?> GetByIdAsync(
        Guid workflowInstanceId,
        CancellationToken cancellationToken);

    Task AddAsync(
        WorkflowInstanceState instance,
        CancellationToken cancellationToken);

    Task UpdateAsync(
        WorkflowInstanceState instance,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<WorkflowInstanceState>> GetWaitingForResumeAsync(
        DateTime utcNow,
        CancellationToken cancellationToken);
}