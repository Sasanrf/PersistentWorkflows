namespace PersistentWorkflows.Abstractions.Definitions;

/// <summary>Deterministic, versioned metadata describing ordered application actions.</summary>
public interface IWorkflowDefinition<TContext>
    where TContext : class
{
    /// <summary>Stable business workflow name, at most 200 characters.</summary>
    string Name { get; }
    /// <summary>Definition version retained for the lifetime of instances started with it.</summary>
    int Version => 1;
    /// <summary>Persisted context schema version; changes require an explicit migrator.</summary>
    int ContextSchemaVersion => 1;

    /// <summary>Describe actions without performing business effects.</summary>
    void Build(IWorkflowBuilder<TContext> builder);
}
