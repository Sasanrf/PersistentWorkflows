using PersistentWorkflows.Abstractions.Definitions;

namespace PersistentWorkflows.EntityFrameworkCore.Tests.TestDoubles;

public sealed class WaitingRunnerWorkflow : IWorkflowDefinition<RunnerWorkflowContext>
{
    public string Name => "waiting-runner-workflow";

    public void Build(IWorkflowBuilder<RunnerWorkflowContext> builder)
    {
        builder
            .Step<FirstRunnerStep>("step-1")
            .Step<WaitingRunnerStep>("step-2")
            .Step<SecondRunnerStep>("step-3");
    }
}