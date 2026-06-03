using FluentAssertions;
using PersistentWorkflows.Core.Definitions;
using PersistentWorkflows.Core.Registry;
using Xunit;

namespace PersistentWorkflows.Core.Tests.Registry;

public sealed class WorkflowDefinitionRegistryTests
{
    [Fact]
    public void Register_Should_Store_Descriptor()
    {
        var registry = new WorkflowDefinitionRegistry();

        var descriptor = new WorkflowDefinitionDescriptor
        {
            Name = "test-workflow",
            WorkflowType = typeof(string),
            ContextType = typeof(int),
            Steps =
            [
                new WorkflowStepDefinition
                {
                    Name = "step-1",
                    StepType = typeof(object)
                }
            ]
        };

        registry.Register(descriptor);

        var result = registry.Get(typeof(string));

        result.Should().BeSameAs(descriptor);
    }

    [Fact]
    public void Register_Should_Throw_When_Workflow_Is_Already_Registered()
    {
        var registry = new WorkflowDefinitionRegistry();

        var descriptor = new WorkflowDefinitionDescriptor
        {
            Name = "test-workflow",
            WorkflowType = typeof(string),
            ContextType = typeof(int),
            Steps =
            [
                new WorkflowStepDefinition
                {
                    Name = "step-1",
                    StepType = typeof(object)
                }
            ]
        };

        registry.Register(descriptor);

        var action = () => registry.Register(descriptor);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*already registered*");
    }

    [Fact]
    public void Get_Should_Throw_When_Workflow_Is_Not_Registered()
    {
        var registry = new WorkflowDefinitionRegistry();

        var action = () => registry.Get(typeof(string));

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*is not registered*");
    }

    [Fact]
    public void TryGet_Should_Return_False_When_Workflow_Is_Not_Registered()
    {
        var registry = new WorkflowDefinitionRegistry();

        var result = registry.TryGet(typeof(string), out var descriptor);

        result.Should().BeFalse();
        descriptor.Should().BeNull();
    }

    [Fact]
    public void GetByName_Should_Return_Descriptor_When_Name_Is_Registered()
    {
        var registry = new WorkflowDefinitionRegistry();

        var descriptor = new WorkflowDefinitionDescriptor
        {
            Name = "test-workflow",
            WorkflowType = typeof(string),
            ContextType = typeof(int),
            Steps =
            [
                new WorkflowStepDefinition
                {
                    Name = "step-1",
                    StepType = typeof(object)
                }
            ]
        };

        registry.Register(descriptor);

        var result = registry.GetByName("test-workflow");

        result.Should().BeSameAs(descriptor);
    }

    [Fact]
    public void Register_Should_Throw_When_Workflow_Name_Is_Already_Registered()
    {
        var registry = new WorkflowDefinitionRegistry();

        var first = new WorkflowDefinitionDescriptor
        {
            Name = "same-name",
            WorkflowType = typeof(string),
            ContextType = typeof(int),
            Steps =
            [
                new WorkflowStepDefinition
                {
                    Name = "step-1",
                    StepType = typeof(object)
                }
            ]
        };

        var second = new WorkflowDefinitionDescriptor
        {
            Name = "same-name",
            WorkflowType = typeof(Guid),
            ContextType = typeof(int),
            Steps =
            [
                new WorkflowStepDefinition
                {
                    Name = "step-1",
                    StepType = typeof(object)
                }
            ]
        };

        registry.Register(first);

        var action = () => registry.Register(second);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*already registered*");
    }
}