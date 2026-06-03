using PersistentWorkflows.Abstractions.Definitions;

namespace PersistentWorkflows.EntityFrameworkCore.Tests.TestDoubles;

public sealed class ResumableRunnerWorkflow : IWorkflowDefinition<RunnerWorkflowContext>
{
    public string Name => "resumable-runner-workflow";

    public void Build(IWorkflowBuilder<RunnerWorkflowContext> builder)
    {
        builder
            .Step<FirstRunnerStep>("step-1")
            .Step<ResumableRunnerStep>("step-2")
            .Step<SecondRunnerStep>("step-3");
    }
}