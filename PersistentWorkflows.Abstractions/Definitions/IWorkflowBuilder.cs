namespace PersistentWorkflows.Abstractions.Definitions;

/// <summary>Build a sequence of uniquely named actions.</summary>
public interface IWorkflowBuilder<TContext>
    where TContext : class
{
    /// <summary>Append a scoped action. Names beginning with '$' are reserved for engine audit events.</summary>
    IWorkflowBuilder<TContext> Step<TStep>(string stepName)
        where TStep : class, IWorkflowStep<TContext>;
}
