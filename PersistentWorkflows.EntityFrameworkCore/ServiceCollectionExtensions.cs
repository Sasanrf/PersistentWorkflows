using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PersistentWorkflows.Abstractions.Persistence;
using PersistentWorkflows.EntityFrameworkCore.Persistence;

namespace PersistentWorkflows.EntityFrameworkCore;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPersistentWorkflowsEntityFrameworkCore(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> configureDbContext)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureDbContext);

        services.AddDbContext<PersistentWorkflowsDbContext>(configureDbContext);

        services.AddScoped<IWorkflowInstanceRepository, WorkflowInstanceRepository>();
        services.AddScoped<IWorkflowStepExecutionRepository, WorkflowStepExecutionRepository>();

        return services;
    }
}