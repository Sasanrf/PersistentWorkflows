using PersistentWorkflows.Abstractions.Execution;
using PersistentWorkflows.Abstractions.Results;

namespace PersistentWorkflows.Abstractions.Definitions;

public interface IWorkflowStep<TContext>
    where TContext : class
{
    Task<StepResult> ExecuteAsync(
        WorkflowExecutionContext<TContext> context,
        CancellationToken cancellationToken);
}