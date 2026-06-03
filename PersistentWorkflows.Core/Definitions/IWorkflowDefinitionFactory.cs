namespace PersistentWorkflows.Core.Definitions;

internal interface IWorkflowDefinitionFactory
{
    WorkflowDefinitionDescriptor Create(Type workflowType, Type contextType);
}