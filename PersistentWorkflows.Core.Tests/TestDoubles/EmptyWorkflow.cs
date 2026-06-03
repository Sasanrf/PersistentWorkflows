using PersistentWorkflows.Abstractions.Definitions;

namespace PersistentWorkflows.Core.Tests.TestDoubles;

public sealed class EmptyWorkflow : IWorkflowDefinition<FakeWorkflowContext>
{
    public string Name => "empty-workflow";

    public void Build(IWorkflowBuilder<FakeWorkflowContext> builder)
    {
    }
}