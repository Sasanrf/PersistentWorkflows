using PersistentWorkflows.Abstractions.Serialization;

namespace PersistentWorkflows.Core.Serialization;

internal sealed class DefaultWorkflowContextMigrator : IWorkflowContextMigrator
{
    public string Migrate(string workflowName, int definitionVersion, int fromVersion, int toVersion, string contextJson) =>
        fromVersion == toVersion ? contextJson : throw new InvalidOperationException($"Context migration required for '{workflowName}': {fromVersion} to {toVersion}. Register IWorkflowContextMigrator.");
}
