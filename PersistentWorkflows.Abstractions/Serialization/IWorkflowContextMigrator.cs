namespace PersistentWorkflows.Abstractions.Serialization;

/// <summary>Migrate persisted JSON explicitly. Never reorder actions through a context migration.</summary>
public interface IWorkflowContextMigrator
{
    string Migrate(string workflowName, int definitionVersion, int fromVersion, int toVersion, string contextJson);
}
