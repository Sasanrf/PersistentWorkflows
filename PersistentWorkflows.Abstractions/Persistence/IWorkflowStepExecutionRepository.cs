namespace PersistentWorkflows.Abstractions.Persistence;

public interface IWorkflowStepExecutionRepository
{
    Task AddAsync(
        WorkflowStepExecutionState stepExecution,
        CancellationToken cancellationToken);

    Task UpdateAsync(
        WorkflowStepExecutionState stepExecution,
        CancellationToken cancellationToken);

    Task<int> GetNextAttemptAsync(
        Guid workflowInstanceId,
        string stepName,
        CancellationToken cancellationToken);
}