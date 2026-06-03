using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace PersistentWorkflows.EntityFrameworkCore.Persistence;

public static class PersistentWorkflowsDatabaseInitializer
{
    public static async Task EnsurePersistentWorkflowsDatabaseAsync(
        this IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        using var scope = serviceProvider.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<PersistentWorkflowsDbContext>();

        await dbContext.Database.MigrateAsync(cancellationToken);
    }
}