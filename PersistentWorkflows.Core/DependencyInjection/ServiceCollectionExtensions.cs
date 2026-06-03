using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PersistentWorkflows.Abstractions.Definitions;
using PersistentWorkflows.Abstractions.Execution;
using PersistentWorkflows.Abstractions.Serialization;
using PersistentWorkflows.Core.Definitions;
using PersistentWorkflows.Core.Execution;
using PersistentWorkflows.Core.Registry;
using PersistentWorkflows.Core.Serialization;

namespace PersistentWorkflows.Core.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPersistentWorkflows(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<PersistentWorkflowsOptions>();

        services.TryAddSingleton<IWorkflowDefinitionRegistry, WorkflowDefinitionRegistry>();
        services.TryAddSingleton<IWorkflowDefinitionFactory, WorkflowDefinitionFactory>();
        services.TryAddSingleton<IWorkflowRegistryInitializer, WorkflowRegistryInitializer>();

        services.AddScoped<IWorkflowRunner, WorkflowRunner>();
        services.TryAddSingleton<IWorkflowContextSerializer, SystemTextJsonWorkflowContextSerializer>();

        return services;
    }

    public static IServiceCollection AddWorkflow<TWorkflow, TContext>(this IServiceCollection services)
        where TWorkflow : class, IWorkflowDefinition<TContext>
        where TContext : class
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<TWorkflow>();

        services.Configure<PersistentWorkflowsOptions>(options =>
        {
            options.Registrations.Add(new WorkflowRegistration
            {
                WorkflowType = typeof(TWorkflow),
                ContextType = typeof(TContext)
            });
        });

        return services;
    }

    public static IServiceCollection AddPersistentWorkflowsBackgroundResumer(
        this IServiceCollection services,
        Action<PersistentWorkflowsWorkerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.AddScoped<IWorkflowResumer, WorkflowResumer>();
        services.AddHostedService<WorkflowResumerBackgroundService>();

        return services;
    }
}