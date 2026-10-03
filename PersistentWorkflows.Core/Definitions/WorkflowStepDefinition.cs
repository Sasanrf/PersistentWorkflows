namespace PersistentWorkflows.Core.Definitions;

internal sealed class WorkflowStepDefinition
{
    public required string Name { get; init; }
    public required Type StepType { get; init; }
    public Func<IServiceProvider, Abstractions.Persistence.WorkflowInstanceState, Abstractions.Serialization.IWorkflowContextSerializer, int, string?, CancellationToken, Task<StepInvocation>>? InvokeAsync { get; init; }
}

internal sealed record StepInvocation(Abstractions.Results.StepResult Result, string ContextJson);
