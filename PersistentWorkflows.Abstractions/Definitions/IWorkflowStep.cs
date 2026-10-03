using PersistentWorkflows.Abstractions.Execution;
using PersistentWorkflows.Abstractions.Results;

namespace PersistentWorkflows.Abstractions.Definitions;

/// <summary>An application action that may execute again after interruption. External effects should be idempotent.</summary>
public interface IWorkflowStep<TContext>
    where TContext : class
{
    /// <summary>Execute an action and report its durable outcome. Honor cancellation cooperatively.</summary>
    Task<StepResult> ExecuteAsync(
        WorkflowExecutionContext<TContext> context,
        CancellationToken cancellationToken);
}
