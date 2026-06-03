using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PersistentWorkflows.Core.Definitions;
using PersistentWorkflows.Core.DependencyInjection;
using PersistentWorkflows.Core.Registry;
using PersistentWorkflows.Core.Tests.TestDoubles;
using Xunit;

namespace PersistentWorkflows.Core.Tests.Registry;

public sealed class WorkflowRegistryInitializerTests
{
    [Fact]
    public void Initialize_Should_Register_Configured_Workflows()
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.AddScoped<FakeWorkflow>();
        services.AddSingleton<IWorkflowDefinitionFactory, WorkflowDefinitionFactory>();
        services.AddSingleton<IWorkflowDefinitionRegistry, WorkflowDefinitionRegistry>();

        services.Configure<PersistentWorkflowsOptions>(options =>
        {
            options.Registrations.Add(new WorkflowRegistration
            {
                WorkflowType = typeof(FakeWorkflow),
                ContextType = typeof(FakeWorkflowContext)
            });
        });

        using var provider = services.BuildServiceProvider();

        var initializer = new WorkflowRegistryInitializer(
            provider.GetRequiredService<IOptions<PersistentWorkflowsOptions>>(),
            provider.GetRequiredService<IWorkflowDefinitionFactory>(),
            provider.GetRequiredService<IWorkflowDefinitionRegistry>());

        initializer.Initialize();

        var registry = provider.GetRequiredService<IWorkflowDefinitionRegistry>();
        var descriptor = registry.Get(typeof(FakeWorkflow));

        descriptor.Name.Should().Be("fake-workflow");
        descriptor.Steps.Should().HaveCount(2);
    }

    [Fact]
    public void Initialize_Should_Run_Only_Once()
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.AddScoped<FakeWorkflow>();
        services.AddSingleton<IWorkflowDefinitionFactory, WorkflowDefinitionFactory>();
        services.AddSingleton<IWorkflowDefinitionRegistry, WorkflowDefinitionRegistry>();

        services.Configure<PersistentWorkflowsOptions>(options =>
        {
            options.Registrations.Add(new WorkflowRegistration
            {
                WorkflowType = typeof(FakeWorkflow),
                ContextType = typeof(FakeWorkflowContext)
            });
        });

        using var provider = services.BuildServiceProvider();

        var initializer = new WorkflowRegistryInitializer(
            provider.GetRequiredService<IOptions<PersistentWorkflowsOptions>>(),
            provider.GetRequiredService<IWorkflowDefinitionFactory>(),
            provider.GetRequiredService<IWorkflowDefinitionRegistry>());

        initializer.Initialize();
        initializer.Initialize();

        var registry = provider.GetRequiredService<IWorkflowDefinitionRegistry>();
        var descriptor = registry.Get(typeof(FakeWorkflow));

        descriptor.Should().NotBeNull();
    }
}