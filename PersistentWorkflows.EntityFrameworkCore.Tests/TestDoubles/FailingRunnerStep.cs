using PersistentWorkflows.Abstractions.Definitions;
using PersistentWorkflows.Abstractions.Execution;
using PersistentWorkflows.Abstractions.Results;

namespace PersistentWorkflows.EntityFrameworkCore.Tests.TestDoubles;

public sealed class FailingRunnerStep : IWorkflowStep<RunnerWorkflowContext>
{
    public Task<StepResult> ExecuteAsync(
        WorkflowExecutionContext<RunnerWorkflowContext> context,
        CancellationToken cancellationToken)
    {
        context.Data.ExecutedSteps.Add("failing-step");
        return Task.FromResult(StepResult.Fail("failed", "step failed"));
    }
}