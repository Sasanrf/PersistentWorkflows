using PersistentWorkflows.Abstractions.Definitions;

namespace PersistentWorkflows.Core.Tests.TestDoubles;

public sealed class FakeWorkflow : IWorkflowDefinition<FakeWorkflowContext>
{
    public string Name => "fake-workflow";

    public void Build(IWorkflowBuilder<FakeWorkflowContext> builder)
    {
        builder
            .Step<FakeWorkflowStep>("step-1")
            .Step<AnotherFakeWorkflowStep>("step-2");
    }
}