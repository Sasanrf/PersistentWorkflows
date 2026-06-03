namespace PersistentWorkflows.Core.Definitions;

internal sealed class WorkflowDefinitionDescriptor
{
    public required string Name { get; init; }
    public required Type WorkflowType { get; init; }
    public required Type ContextType { get; init; }
    public required IReadOnlyList<WorkflowStepDefinition> Steps { get; init; }
}