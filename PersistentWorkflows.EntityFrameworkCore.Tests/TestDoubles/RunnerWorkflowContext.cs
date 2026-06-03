namespace PersistentWorkflows.EntityFrameworkCore.Tests.TestDoubles;

public sealed class RunnerWorkflowContext
{
    public List<string> ExecutedSteps { get; set; } = [];
}