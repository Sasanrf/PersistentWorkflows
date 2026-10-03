namespace PersistentWorkflows.Abstractions.Persistence;

public sealed record WorkflowSignalState(string Name, string? PayloadJson, DateTime ReceivedAtUtc);
