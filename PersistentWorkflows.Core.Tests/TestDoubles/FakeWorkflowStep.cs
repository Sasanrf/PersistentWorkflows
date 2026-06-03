using PersistentWorkflows.Abstractions.Definitions;
using PersistentWorkflows.Abstractions.Execution;
using PersistentWorkflows.Abstractions.Results;

namespace PersistentWorkflows.Core.Tests.TestDoubles;

public sealed class FakeWorkflowStep : IWorkflowStep<FakeWorkflowContext>
{
    public Task<StepResult> ExecuteAsync(
        WorkflowExecutionContext<FakeWorkflowContext> context,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(StepResult.Success());
    }
}