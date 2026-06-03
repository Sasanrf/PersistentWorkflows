using PersistentWorkflows.Abstractions.Definitions;

namespace PersistentWorkflows.Core.Tests.TestDoubles;

public sealed class InvalidNameWorkflow : IWorkflowDefinition<FakeWorkflowContext>
{
    public string Name => string.Empty;

    public void Build(IWorkflowBuilder<FakeWorkflowContext> builder)
    {
        builder.Step<FakeWorkflowStep>("step-1");
    }
}