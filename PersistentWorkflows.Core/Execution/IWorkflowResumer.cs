namespace PersistentWorkflows.Core.Execution;

internal interface IWorkflowResumer
{
    Task ProcessOnceAsync(CancellationToken cancellationToken);
}