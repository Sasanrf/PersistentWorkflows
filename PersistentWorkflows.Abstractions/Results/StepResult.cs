using PersistentWorkflows.Abstractions.Enums;

namespace PersistentWorkflows.Abstractions.Results;

public sealed class StepResult
{
    public StepExecutionStatus Status { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public TimeSpan? RetryAfter { get; init; }

    public static StepResult Success() =>
        new() { Status = StepExecutionStatus.Succeeded };

    public static StepResult Retry(string? message = null, TimeSpan? retryAfter = null) =>
        new()
        {
            Status = StepExecutionStatus.RetryableFailure,
            ErrorMessage = message,
            RetryAfter = retryAfter
        };

    public static StepResult Fail(string? code = null, string? message = null) =>
        new()
        {
            Status = StepExecutionStatus.Failed,
            ErrorCode = code,
            ErrorMessage = message
        };

    public static StepResult Wait(TimeSpan? retryAfter = null) =>
        new()
        {
            Status = StepExecutionStatus.Waiting,
            RetryAfter = retryAfter
        };
}