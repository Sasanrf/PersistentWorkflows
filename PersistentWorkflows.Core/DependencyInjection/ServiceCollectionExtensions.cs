using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PersistentWorkflows.Abstractions.Definitions;
using PersistentWorkflows.Abstractions.Execution;
using PersistentWorkflows.Abstractions.Persistence;
using PersistentWorkflows.Abstractions.Serialization;
using PersistentWorkflows.Core.Definitions;
using PersistentWorkflows.Core.Execution;
using PersistentWorkflows.Core.Registry;
using PersistentWorkflows.Core.Serialization;

namespace PersistentWorkflows.Core.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPersistentWorkflows(this IServiceCollection services, Action<PersistentWorkflowsOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<PersistentWorkflowsOptions>()
            .Validate(x => x.LeaseDuration > TimeSpan.Zero && x.LeaseDuration <= TimeSpan.FromDays(30) && x.LeaseRenewalInterval > TimeSpan.Zero && x.LeaseRenewalInterval < x.LeaseDuration / 2, "Lease renewal must be positive and less than half the lease duration; leases cannot exceed 30 days.")
            .Validate(x => x.StepTimeout > TimeSpan.Zero && x.StepTimeout <= TimeSpan.FromDays(30) && x.MaximumAttempts > 0 && x.InitialRetryDelay > TimeSpan.Zero && x.MaximumRetryDelay >= x.InitialRetryDelay && x.MaximumRetryDelay <= TimeSpan.FromDays(30) && x.RetryJitterRatio is >= 0 and <= 1 && x.IsRetryableException is not null, "Invalid execution policy; timeout and retry limits cannot exceed 30 days.")
            .ValidateOnStart();
        if (configure is not null) services.Configure(configure);

        services.TryAddSingleton<IWorkflowDefinitionRegistry, WorkflowDefinitionRegistry>();
        services.TryAddSingleton<IWorkflowDefinitionFactory, WorkflowDefinitionFactory>();
        services.TryAddSingleton<IWorkflowRegistryInitializer, WorkflowRegistryInitializer>();
        services.AddScoped<WorkflowRunner>();
        services.AddScoped<IWorkflowRunner>(sp => sp.GetRequiredService<WorkflowRunner>());
        services.AddScoped<IWorkflowProcessor>(sp => sp.GetRequiredService<WorkflowRunner>());
        services.TryAddSingleton<IWorkflowContextSerializer, SystemTextJsonWorkflowContextSerializer>();
        services.TryAddSingleton<IWorkflowContextMigrator, DefaultWorkflowContextMigrator>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddLogging();
        services.AddHostedService<WorkflowValidationService>();

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
        services.AddOptions<PersistentWorkflowsWorkerOptions>()
            .Validate(x => x.BatchSize > 0 && x.MaximumConcurrency > 0 && x.PollingInterval > TimeSpan.Zero && x.PollingInterval <= TimeSpan.FromDays(30), "Worker batch size, concurrency, and polling interval must be positive; polling cannot exceed 30 days.")
            .ValidateOnStart();
        services.AddHostedService<WorkflowResumerBackgroundService>();

        return services;
    }
}
