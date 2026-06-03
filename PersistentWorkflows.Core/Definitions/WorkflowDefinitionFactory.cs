using PersistentWorkflows.Abstractions.Definitions;

namespace PersistentWorkflows.Core.Definitions;

internal sealed class WorkflowDefinitionFactory : IWorkflowDefinitionFactory
{
    private readonly IServiceProvider _serviceProvider;

    public WorkflowDefinitionFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public WorkflowDefinitionDescriptor Create(Type workflowType, Type contextType)
    {
        ArgumentNullException.ThrowIfNull(workflowType);
        ArgumentNullException.ThrowIfNull(contextType);

        var workflow = _serviceProvider.GetService(workflowType);

        if (workflow is null)
        {
            throw new InvalidOperationException(
                $"Workflow '{workflowType.FullName}' is not registered in the service provider.");
        }

        var workflowInterface = typeof(IWorkflowDefinition<>).MakeGenericType(contextType);

        if (!workflowInterface.IsAssignableFrom(workflowType))
        {
            throw new InvalidOperationException(
                $"Workflow '{workflowType.FullName}' does not implement IWorkflowDefinition<{contextType.Name}>.");
        }

        var nameProperty = workflowType.GetProperty(nameof(IWorkflowDefinition<object>.Name));

        if (nameProperty is null)
        {
            throw new InvalidOperationException(
                $"Workflow '{workflowType.FullName}' must expose a Name property.");
        }

        var workflowName = nameProperty.GetValue(workflow) as string;

        if (string.IsNullOrWhiteSpace(workflowName))
        {
            throw new InvalidOperationException(
                $"Workflow '{workflowType.FullName}' must have a non-empty name.");
        }

        var builderType = typeof(WorkflowBuilder<>).MakeGenericType(contextType);
        var builder = Activator.CreateInstance(builderType);

        if (builder is null)
        {
            throw new InvalidOperationException(
                $"Failed to create workflow builder for context type '{contextType.FullName}'.");
        }

        var buildMethod = workflowType.GetMethod(nameof(IWorkflowDefinition<object>.Build));

        if (buildMethod is null)
        {
            throw new InvalidOperationException(
                $"Workflow '{workflowType.FullName}' must define a Build method.");
        }

        buildMethod.Invoke(workflow, new[] { builder });

        var stepsMethod = builderType.GetMethod(nameof(WorkflowBuilder<object>.Build));

        if (stepsMethod is null)
        {
            throw new InvalidOperationException("Failed to access workflow builder result.");
        }

        var steps = stepsMethod.Invoke(builder, null) as IReadOnlyList<WorkflowStepDefinition>;

        if (steps is null || steps.Count == 0)
        {
            throw new InvalidOperationException(
                $"Workflow '{workflowType.FullName}' must define at least one step.");
        }

        return new WorkflowDefinitionDescriptor
        {
            Name = workflowName,
            WorkflowType = workflowType,
            ContextType = contextType,
            Steps = steps
        };
    }
}