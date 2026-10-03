using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PersistentWorkflows.Core.DependencyInjection;
using PersistentWorkflows.EntityFrameworkCore.Persistence;

namespace PersistentWorkflows.EntityFrameworkCore.SqlServer;

public static class ServiceCollectionExtensions
{
    /// <summary>Register the engine, SQL Server store, and optionally the background worker in one call.</summary>
    public static IServiceCollection AddPersistentWorkflowsSqlServer(this IServiceCollection services, string connectionString,
        Action<PersistentWorkflowsOptions>? configureExecution = null, Action<PersistentWorkflowsWorkerOptions>? configureWorker = null,
        bool enableWorker = true)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        services.AddPersistentWorkflows(configureExecution);
        services.AddPersistentWorkflowsEntityFrameworkCore(o => o.UseSqlServer(connectionString,
            sql => sql.MigrationsAssembly(typeof(ServiceCollectionExtensions).Assembly.GetName().Name)));
        if (enableWorker) services.AddPersistentWorkflowsBackgroundResumer(configureWorker);
        return services;
    }
}
