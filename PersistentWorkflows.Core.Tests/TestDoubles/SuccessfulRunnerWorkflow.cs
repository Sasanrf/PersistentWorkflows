using PersistentWorkflows.Abstractions.Definitions;

namespace PersistentWorkflows.Core.Tests.TestDoubles;

public sealed class SuccessfulRunnerWorkflow : IWorkflowDefinition<RunnerWorkflowContext>
{
    public string Name => "successful-runner-workflow";

    public void Build(IWorkflowBuilder<RunnerWorkflowContext> builder)
    {
        builder
            .Step<FirstRunnerStep>("step-1")
            .Step<SecondRunnerStep>("step-2");
    }
}