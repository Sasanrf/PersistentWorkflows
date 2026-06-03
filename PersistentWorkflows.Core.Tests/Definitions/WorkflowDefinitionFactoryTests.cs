using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PersistentWorkflows.Core.Definitions;
using PersistentWorkflows.Core.Tests.TestDoubles;
using Xunit;

namespace PersistentWorkflows.Core.Tests.Definitions;

public sealed class WorkflowDefinitionFactoryTests
{
    [Fact]
    public void Create_Should_Build_Descriptor_For_Valid_Workflow()
    {
        var services = new ServiceCollection();
        services.AddScoped<FakeWorkflow>();

        using var provider = services.BuildServiceProvider();
        var factory = new WorkflowDefinitionFactory(provider);

        var descriptor = factory.Create(typeof(FakeWorkflow), typeof(FakeWorkflowContext));

        descriptor.Name.Should().Be("fake-workflow");
        descriptor.WorkflowType.Should().Be(typeof(FakeWorkflow));
        descriptor.ContextType.Should().Be(typeof(FakeWorkflowContext));
        descriptor.Steps.Should().HaveCount(2);
        descriptor.Steps[0].Name.Should().Be("step-1");
        descriptor.Steps[1].Name.Should().Be("step-2");
    }

    [Fact]
    public void Create_Should_Throw_When_Workflow_Is_Not_Registered_In_ServiceProvider()
    {
        var services = new ServiceCollection();

        using var provider = services.BuildServiceProvider();
        var factory = new WorkflowDefinitionFactory(provider);

        var action = () => factory.Create(typeof(FakeWorkflow), typeof(FakeWorkflowContext));

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*is not registered in the service provider*");
    }

    [Fact]
    public void Create_Should_Throw_When_Workflow_Name_Is_Empty()
    {
        var services = new ServiceCollection();
        services.AddScoped<InvalidNameWorkflow>();

        using var provider = services.BuildServiceProvider();
        var factory = new WorkflowDefinitionFactory(provider);

        var action = () => factory.Create(typeof(InvalidNameWorkflow), typeof(FakeWorkflowContext));

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*must have a non-empty name*");
    }

    [Fact]
    public void Create_Should_Throw_When_Workflow_Has_No_Steps()
    {
        var services = new ServiceCollection();
        services.AddScoped<EmptyWorkflow>();

        using var provider = services.BuildServiceProvider();
        var factory = new WorkflowDefinitionFactory(provider);

        var action = () => factory.Create(typeof(EmptyWorkflow), typeof(FakeWorkflowContext));

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*must define at least one step*");
    }
}