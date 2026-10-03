using Microsoft.Extensions.Options;
using PersistentWorkflows.Core.Definitions;
using PersistentWorkflows.Core.DependencyInjection;

namespace PersistentWorkflows.Core.Registry;

internal sealed class WorkflowRegistryInitializer : IWorkflowRegistryInitializer
{
    private readonly PersistentWorkflowsOptions _options;
    private readonly IWorkflowDefinitionFactory _factory;
    private readonly IWorkflowDefinitionRegistry _registry;
    private readonly SemaphoreSlim _initialization = new(1, 1);

    private volatile bool _initialized;

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
        => InitializeAsync().GetAwaiter().GetResult();

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        await _initialization.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return;
            }

            var descriptors = new List<WorkflowDefinitionDescriptor>();
            foreach (var registration in _options.Registrations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                descriptors.Add(await _factory.CreateAsync(registration.WorkflowType, registration.ContextType).ConfigureAwait(false));
            }

            if (descriptors.GroupBy(x => x.WorkflowType).Any(x => x.Count() > 1) ||
                descriptors.GroupBy(x => $"{x.Name}/{x.Version}", StringComparer.OrdinalIgnoreCase).Any(x => x.Count() > 1))
                throw new InvalidOperationException("Duplicate workflow definition registration.");

            foreach (var descriptor in descriptors)
            {
                _registry.Register(descriptor);
            }

            _initialized = true;
        }
        finally { _initialization.Release(); }
    }
}
