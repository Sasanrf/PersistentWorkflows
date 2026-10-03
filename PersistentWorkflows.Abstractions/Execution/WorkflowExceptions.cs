namespace PersistentWorkflows.Abstractions.Execution;

public sealed class WorkflowInputConflictException(string message) : InvalidOperationException(message);
public sealed class WorkflowLeaseLostException() : InvalidOperationException("Workflow execution ownership was lost.");
