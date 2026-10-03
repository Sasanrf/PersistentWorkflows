using System.Reflection;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;
using PersistentWorkflows.Abstractions.Definitions;

namespace PersistentWorkflows.Core.Definitions;

internal sealed class WorkflowDefinitionFactory(IServiceProvider serviceProvider) : IWorkflowDefinitionFactory
{
    public WorkflowDefinitionDescriptor Create(Type workflowType, Type contextType)
        => CreateAsync(workflowType, contextType).GetAwaiter().GetResult();

    public async Task<WorkflowDefinitionDescriptor> CreateAsync(Type workflowType, Type contextType)
    {
        ArgumentNullException.ThrowIfNull(workflowType);
        ArgumentNullException.ThrowIfNull(contextType);
        if (!typeof(IWorkflowDefinition<>).MakeGenericType(contextType).IsAssignableFrom(workflowType))
            throw new InvalidOperationException($"Workflow '{workflowType.FullName}' does not implement the requested definition interface.");
        try
        {
            return await ((Task<WorkflowDefinitionDescriptor>)typeof(WorkflowDefinitionFactory)
                .GetMethod(nameof(CreateTyped), BindingFlags.NonPublic | BindingFlags.Instance)!
                .MakeGenericMethod(contextType).Invoke(this, [workflowType])!).ConfigureAwait(false);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private async Task<WorkflowDefinitionDescriptor> CreateTyped<TContext>(Type workflowType) where TContext : class
    {
        var scope = serviceProvider.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var workflow = scope.ServiceProvider.GetService(workflowType) as IWorkflowDefinition<TContext>
                ?? throw new InvalidOperationException($"Workflow '{workflowType.FullName}' is not registered in the service provider.");
            if (string.IsNullOrWhiteSpace(workflow.Name) || workflow.Name.Length > 200)
                throw new InvalidOperationException($"Workflow '{workflowType.FullName}' must have a non-empty name of at most 200 characters.");
            if (workflow.Version < 1 || workflow.ContextSchemaVersion < 1)
                throw new InvalidOperationException("Definition and context schema versions must be positive.");
            var builder = new WorkflowBuilder<TContext>();
            workflow.Build(builder);
            var steps = builder.Build();
            if (steps.Count == 0) throw new InvalidOperationException($"Workflow '{workflowType.FullName}' must define at least one step.");
            return new WorkflowDefinitionDescriptor
            {
                Name = workflow.Name, WorkflowType = workflowType, ContextType = typeof(TContext),
                Steps = steps.ToArray(), Version = workflow.Version, ContextSchemaVersion = workflow.ContextSchemaVersion
            };
        }
    }
}
