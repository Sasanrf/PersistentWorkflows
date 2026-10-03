using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PersistentWorkflows.Abstractions.Persistence;
using PersistentWorkflows.Core.DependencyInjection;
using PersistentWorkflows.Core.Registry;

namespace PersistentWorkflows.Core.Execution;

internal sealed class WorkflowValidationService(IServiceScopeFactory scopes, IWorkflowRegistryInitializer initializer, IWorkflowDefinitionRegistry registry, IOptions<PersistentWorkflowsOptions> options) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await initializer.InitializeAsync(cancellationToken);
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IWorkflowStore>();
        foreach (var registration in options.Value.Registrations)
            foreach (var step in registry.Get(registration.WorkflowType).Steps)
                scope.ServiceProvider.GetRequiredService(step.StepType);
    }
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
