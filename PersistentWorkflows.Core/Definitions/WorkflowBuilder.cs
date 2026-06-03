using PersistentWorkflows.Abstractions.Definitions;

namespace PersistentWorkflows.Core.Definitions;

internal sealed class WorkflowBuilder<TContext> : IWorkflowBuilder<TContext>
    where TContext : class
{
    private readonly List<WorkflowStepDefinition> _steps = new();

    public IWorkflowBuilder<TContext> Step<TStep>(string stepName)
        where TStep : class, IWorkflowStep<TContext>
    {
        if (string.IsNullOrWhiteSpace(stepName))
        {
            throw new ArgumentException("Step name cannot be null or empty.", nameof(stepName));
        }

        if (_steps.Any(x => string.Equals(x.Name, stepName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"A step with the name '{stepName}' has already been added.");
        }

        _steps.Add(new WorkflowStepDefinition
        {
            Name = stepName,
            StepType = typeof(TStep)
        });

        return this;
    }

    public IReadOnlyList<WorkflowStepDefinition> Build() => _steps.AsReadOnly();
}