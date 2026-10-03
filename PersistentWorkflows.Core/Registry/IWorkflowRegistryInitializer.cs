namespace PersistentWorkflows.Core.Registry;

internal interface IWorkflowRegistryInitializer
{
    void Initialize();
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
