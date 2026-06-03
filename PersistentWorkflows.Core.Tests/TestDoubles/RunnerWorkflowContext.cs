namespace PersistentWorkflows.Core.Tests.TestDoubles;

public sealed class RunnerWorkflowContext
{
    public List<string> ExecutedSteps { get; set; } = [];
}