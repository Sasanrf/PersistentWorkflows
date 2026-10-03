using PersistentWorkflows.Abstractions.Persistence;

namespace PersistentWorkflows.EntityFrameworkCore.Persistence;

public sealed class WorkflowUnitOfWork(PersistentWorkflowsDbContext dbContext) : IWorkflowUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => dbContext.SaveChangesAsync(cancellationToken);
}
