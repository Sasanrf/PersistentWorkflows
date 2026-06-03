using Microsoft.Extensions.Options;
using PersistentWorkflows.Core.Definitions;
using PersistentWorkflows.Core.DependencyInjection;

namespace PersistentWorkflows.Core.Registry;

internal sealed class WorkflowRegistryInitializer : IWorkflowRegistryInitializer
{
    private readonly PersistentWorkflowsOptions _options;
    private readonly IWorkflowDefinitionFactory _factory;
    private readonly IWorkflowDefinitionRegistry _registry;
    private int _initialized;

    public WorkflowRegistryInitializer(
        IOptions<PersistentWorkflowsOptions> options,
        IWorkflowDefinitionFactory factory,
        IWorkflowDefinitionRegistry registry)
    {
        _options = options.Value;
        _factory = factory;
        _registry = registry;
    }

    public void Initialize()
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 1)
        {
            return;
        }

        foreach (var registration in _options.Registrations)
        {
            var descriptor = _factory.Create(
                registration.WorkflowType,
                registration.ContextType);

            _registry.Register(descriptor);
        }
    }
}