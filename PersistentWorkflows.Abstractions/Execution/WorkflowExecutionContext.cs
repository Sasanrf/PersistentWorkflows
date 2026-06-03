namespace PersistentWorkflows.Abstractions.Execution;

public sealed class WorkflowExecutionContext<TContext>
    where TContext : class
{
    public required Guid WorkflowInstanceId { get; init; }
    public required string WorkflowName { get; init; }
    public required string InstanceKey { get; init; }
    public required string StepName { get; init; }
    public required int Attempt { get; init; }
    public required TContext Data { get; init; }
    public required IServiceProvider Services { get; init; }
}