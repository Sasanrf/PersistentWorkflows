namespace PersistentWorkflows.Core.Registry;

internal sealed class WorkflowRegistration
{
    public Type WorkflowType { get; init; }
    public Type ContextType { get; init; }
}