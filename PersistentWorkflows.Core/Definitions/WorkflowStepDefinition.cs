namespace PersistentWorkflows.Core.Definitions;

internal sealed class WorkflowStepDefinition
{
    public required string Name { get; init; }
    public required Type StepType { get; init; }
}
