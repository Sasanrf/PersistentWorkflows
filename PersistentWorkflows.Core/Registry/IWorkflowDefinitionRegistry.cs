using PersistentWorkflows.Core.Definitions;

namespace PersistentWorkflows.Core.Registry;

internal interface IWorkflowDefinitionRegistry
{
    void Register(WorkflowDefinitionDescriptor descriptor);

    WorkflowDefinitionDescriptor Get(Type workflowType);

    bool TryGet(Type workflowType, out WorkflowDefinitionDescriptor? descriptor);

    WorkflowDefinitionDescriptor GetByName(string workflowName);

    bool TryGetByName(string workflowName, out WorkflowDefinitionDescriptor? descriptor);
    WorkflowDefinitionDescriptor GetByName(string workflowName, int version);
}
