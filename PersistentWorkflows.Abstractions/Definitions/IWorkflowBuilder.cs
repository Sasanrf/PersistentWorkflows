namespace PersistentWorkflows.Abstractions.Definitions;

public interface IWorkflowBuilder<TContext>
    where TContext : class
{
    IWorkflowBuilder<TContext> Step<TStep>(string stepName)
        where TStep : class, IWorkflowStep<TContext>;
}