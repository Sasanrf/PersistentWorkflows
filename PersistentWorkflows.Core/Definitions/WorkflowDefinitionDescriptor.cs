namespace PersistentWorkflows.Core.Definitions;

internal sealed class WorkflowDefinitionDescriptor
{
    public required string Name { get; init; }
    public required Type WorkflowType { get; init; }
    public required Type ContextType { get; init; }
    public required IReadOnlyList<WorkflowStepDefinition> Steps { get; init; }
    public int Version { get; init; } = 1;
    public int ContextSchemaVersion { get; init; } = 1;
    public string Fingerprint => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(string.Join("\n", Steps.Select(x => x.Name + ":" + x.StepType.FullName)))));
}
