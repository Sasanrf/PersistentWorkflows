using FluentAssertions;
using PersistentWorkflows.Core.Definitions;
using PersistentWorkflows.Core.Tests.TestDoubles;
using Xunit;

namespace PersistentWorkflows.Core.Tests.Definitions;

public sealed class WorkflowBuilderTests
{
    [Fact]
    public void Step_Should_Add_Step_When_Name_Is_Valid()
    {
        var builder = new WorkflowBuilder<FakeWorkflowContext>();

        builder.Step<FakeWorkflowStep>("step-1");

        var result = builder.Build();

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("step-1");
        result[0].StepType.Should().Be(typeof(FakeWorkflowStep));
    }

    [Fact]
    public void Step_Should_Throw_When_Name_Is_Empty()
    {
        var builder = new WorkflowBuilder<FakeWorkflowContext>();

        var action = () => builder.Step<FakeWorkflowStep>(string.Empty);

        action.Should().Throw<ArgumentException>()
            .WithMessage("*Step name cannot be null or empty*");
    }

    [Fact]
    public void Step_Should_Throw_When_Name_Is_Duplicate_Ignoring_Case()
    {
        var builder = new WorkflowBuilder<FakeWorkflowContext>();

        builder.Step<FakeWorkflowStep>("step-1");

        var action = () => builder.Step<AnotherFakeWorkflowStep>("STEP-1");

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*already been added*");
    }

    [Fact]
    public void Build_Should_Return_All_Added_Steps_In_Order()
    {
        var builder = new WorkflowBuilder<FakeWorkflowContext>();

        builder
            .Step<FakeWorkflowStep>("step-1")
            .Step<AnotherFakeWorkflowStep>("step-2");

        var result = builder.Build();

        result.Should().HaveCount(2);
        result[0].Name.Should().Be("step-1");
        result[1].Name.Should().Be("step-2");
    }
}