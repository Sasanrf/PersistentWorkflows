using PersistentWorkflows.Abstractions.Definitions;

using Microsoft.Extensions.DependencyInjection;
using PersistentWorkflows.Abstractions.Execution;

namespace PersistentWorkflows.Core.Definitions;

internal sealed class WorkflowBuilder<TContext> : IWorkflowBuilder<TContext>
    where TContext : class
{
    private readonly List<WorkflowStepDefinition> _steps = new();

    public IWorkflowBuilder<TContext> Step<TStep>(string stepName)
        where TStep : class, IWorkflowStep<TContext>
    {
        if (string.IsNullOrWhiteSpace(stepName) || stepName.Length > 200 || stepName.StartsWith('$'))
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
            StepType = typeof(TStep),
            InvokeAsync = async (services, instance, serializer, attempt, payload, ct) =>
            {
                var context = new WorkflowExecutionContext<TContext>
                {
                    WorkflowInstanceId = instance.Id, WorkflowName = instance.WorkflowName,
                    InstanceKey = instance.InstanceKey, StepName = stepName, Attempt = attempt,
                    Data = serializer.Deserialize<TContext>(instance.ContextJson), Services = services,
                    SignalPayloadJson = payload
                };
                var result = await services.GetRequiredService<TStep>().ExecuteAsync(context, ct);
                return new StepInvocation(result, serializer.Serialize(context.Data));
            }
        });

        return this;
    }

    public IReadOnlyList<WorkflowStepDefinition> Build() => _steps.AsReadOnly();
}
