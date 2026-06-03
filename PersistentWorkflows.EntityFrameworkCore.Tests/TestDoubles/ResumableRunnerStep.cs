using PersistentWorkflows.Abstractions.Definitions;
using PersistentWorkflows.Abstractions.Execution;
using PersistentWorkflows.Abstractions.Results;

namespace PersistentWorkflows.EntityFrameworkCore.Tests.TestDoubles;

public sealed class ResumableRunnerStep : IWorkflowStep<RunnerWorkflowContext>
{
    public Task<StepResult> ExecuteAsync(
        WorkflowExecutionContext<RunnerWorkflowContext> context,
        CancellationToken cancellationToken)
    {
        var waitCount = context.Data.ExecutedSteps.Count(x => x == "resumable-wait");

        if (waitCount == 0)
        {
            context.Data.ExecutedSteps.Add("resumable-wait");
            return Task.FromResult(StepResult.Wait(TimeSpan.FromMinutes(1)));
        }

        context.Data.ExecutedSteps.Add("resumable-success");
        return Task.FromResult(StepResult.Success());
    }
}