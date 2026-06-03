using PersistentWorkflows.Core.Definitions;

namespace PersistentWorkflows.Core.Registry;

internal sealed class WorkflowDefinitionRegistry : IWorkflowDefinitionRegistry
{
    private readonly Dictionary<Type, WorkflowDefinitionDescriptor> _descriptorsByType = new();
    private readonly Dictionary<string, WorkflowDefinitionDescriptor> _descriptorsByName =
        new(StringComparer.OrdinalIgnoreCase);

    public void Register(WorkflowDefinitionDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        if (_descriptorsByType.ContainsKey(descriptor.WorkflowType))
        {
            throw new InvalidOperationException(
                $"Workflow '{descriptor.WorkflowType.FullName}' is already registered.");
        }

        if (_descriptorsByName.ContainsKey(descriptor.Name))
        {
            throw new InvalidOperationException(
                $"Workflow name '{descriptor.Name}' is already registered.");
        }

        _descriptorsByType[descriptor.WorkflowType] = descriptor;
        _descriptorsByName[descriptor.Name] = descriptor;
    }

    public WorkflowDefinitionDescriptor Get(Type workflowType)
    {
        ArgumentNullException.ThrowIfNull(workflowType);

        if (!_descriptorsByType.TryGetValue(workflowType, out var descriptor))
        {
            throw new InvalidOperationException(
                $"Workflow '{workflowType.FullName}' is not registered.");
        }

        return descriptor;
    }

    public bool TryGet(Type workflowType, out WorkflowDefinitionDescriptor? descriptor)
    {
        ArgumentNullException.ThrowIfNull(workflowType);

        return _descriptorsByType.TryGetValue(workflowType, out descriptor);
    }

    public WorkflowDefinitionDescriptor GetByName(string workflowName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowName);

        if (!_descriptorsByName.TryGetValue(workflowName, out var descriptor))
        {
            throw new InvalidOperationException(
                $"Workflow name '{workflowName}' is not registered.");
        }

        return descriptor;
    }

    public bool TryGetByName(string workflowName, out WorkflowDefinitionDescriptor? descriptor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowName);

        return _descriptorsByName.TryGetValue(workflowName, out descriptor);
    }
}