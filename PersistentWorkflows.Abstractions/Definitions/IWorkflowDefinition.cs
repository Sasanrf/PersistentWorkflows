namespace PersistentWorkflows.Abstractions.Definitions;

public interface IWorkflowDefinition<TContext>
    where TContext : class
{
    string Name { get; }

    void Build(IWorkflowBuilder<TContext> builder);
}