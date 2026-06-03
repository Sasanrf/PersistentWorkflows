using PersistentWorkflows.Abstractions.Definitions;

namespace PersistentWorkflows.Abstractions.Execution;

public interface IWorkflowRunner
{
    Task<WorkflowStartResult> StartAsync<TWorkflow, TContext>(
        string instanceKey,
        TContext context,
        CancellationToken cancellationToken = default)
        where TWorkflow : class, IWorkflowDefinition<TContext>
        where TContext : class;

    Task ResumeAsync(
        Guid workflowInstanceId,
        CancellationToken cancellationToken = default);
}