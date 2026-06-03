using PersistentWorkflows.Abstractions.Definitions;

namespace PersistentWorkflows.Core.Tests.TestDoubles;

public sealed class FailingRunnerWorkflow : IWorkflowDefinition<RunnerWorkflowContext>
{
    public string Name => "failing-runner-workflow";

    public void Build(IWorkflowBuilder<RunnerWorkflowContext> builder)
    {
        builder
            .Step<FirstRunnerStep>("step-1")
            .Step<FailingRunnerStep>("step-2")
            .Step<SecondRunnerStep>("step-3");
    }
}