namespace PersistentWorkflows.Abstractions.Persistence;

public interface IWorkflowUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}