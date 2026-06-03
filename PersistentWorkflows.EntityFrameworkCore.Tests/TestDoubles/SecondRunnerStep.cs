using PersistentWorkflows.Abstractions.Definitions;
using PersistentWorkflows.Abstractions.Execution;
using PersistentWorkflows.Abstractions.Results;

namespace PersistentWorkflows.EntityFrameworkCore.Tests.TestDoubles;

public sealed class SecondRunnerStep : IWorkflowStep<RunnerWorkflowContext>
{
    private readonly ExecutionTrace _trace;

    public SecondRunnerStep(ExecutionTrace trace)
    {
        _trace = trace;
    }

    public Task<StepResult> ExecuteAsync(
        WorkflowExecutionContext<RunnerWorkflowContext> context,
        CancellationToken cancellationToken)
    {
        context.Data.ExecutedSteps.Add("step-2");
        _trace.Add("step-2");
        return Task.FromResult(StepResult.Success());
    }
}