using PersistentWorkflows.Abstractions.Definitions;
using PersistentWorkflows.Abstractions.Execution;
using PersistentWorkflows.Abstractions.Results;

namespace PersistentWorkflows.Core.Tests.TestDoubles;

public sealed class WaitingRunnerStep : IWorkflowStep<RunnerWorkflowContext>
{
    public Task<StepResult> ExecuteAsync(
        WorkflowExecutionContext<RunnerWorkflowContext> context,
        CancellationToken cancellationToken)
    {
        context.Data.ExecutedSteps.Add("waiting-step");
        return Task.FromResult(StepResult.Wait());
    }
}