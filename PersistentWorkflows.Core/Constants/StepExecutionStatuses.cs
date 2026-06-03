namespace PersistentWorkflows.Core.Constants;

internal static class StepExecutionStatuses
{
    public const string Started = "Started";
    public const string Succeeded = "Succeeded";
    public const string Waiting = "Waiting";
    public const string Failed = "Failed";
    public const string RetryableFailure = "RetryableFailure";
}