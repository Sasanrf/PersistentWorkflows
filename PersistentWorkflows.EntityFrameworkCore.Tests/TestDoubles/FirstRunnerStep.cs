using PersistentWorkflows.Abstractions.Definitions;
using PersistentWorkflows.Abstractions.Execution;
using PersistentWorkflows.Abstractions.Results;

namespace PersistentWorkflows.EntityFrameworkCore.Tests.TestDoubles;

public sealed class FirstRunnerStep : IWorkflowStep<RunnerWorkflowContext>
{
    private readonly ExecutionTrace _trace;

    public FirstRunnerStep(ExecutionTrace trace)
    {
        _trace = trace;
    }

    public Task<StepResult> ExecuteAsync(
        WorkflowExecutionContext<RunnerWorkflowContext> context,
        CancellationToken cancellationToken)
    {
        context.Data.ExecutedSteps.Add("step-1");
        _trace.Add("step-1");
        return Task.FromResult(StepResult.Success());
    }
}