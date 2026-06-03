namespace PersistentWorkflows.Abstractions.Enums;

public enum StepExecutionStatus
{
    Succeeded = 1,
    RetryableFailure = 2,
    Failed = 3,
    Waiting = 4
}