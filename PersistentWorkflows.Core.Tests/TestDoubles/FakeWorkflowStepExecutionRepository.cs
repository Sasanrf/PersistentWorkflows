using PersistentWorkflows.Abstractions.Persistence;

namespace PersistentWorkflows.Core.Tests.TestDoubles;

public sealed class FakeWorkflowStepExecutionRepository : IWorkflowStepExecutionRepository
{
    private readonly List<WorkflowStepExecutionState> _executions = [];

    public IReadOnlyList<WorkflowStepExecutionState> Executions => _executions;

    public Task AddAsync(
        WorkflowStepExecutionState stepExecution,
        CancellationToken cancellationToken)
    {
        _executions.Add(stepExecution);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(
        WorkflowStepExecutionState stepExecution,
        CancellationToken cancellationToken)
    {
        var existing = _executions.First(x => x.Id == stepExecution.Id);

        existing.Status = stepExecution.Status;
        existing.Attempt = stepExecution.Attempt;
        existing.CompletedAtUtc = stepExecution.CompletedAtUtc;
        existing.ErrorCode = stepExecution.ErrorCode;
        existing.ErrorMessage = stepExecution.ErrorMessage;

        return Task.CompletedTask;
    }

    public Task<int> GetNextAttemptAsync(
        Guid workflowInstanceId,
        string stepName,
        CancellationToken cancellationToken)
    {
        var nextAttempt = _executions
            .Where(x => x.WorkflowInstanceId == workflowInstanceId && x.StepName == stepName)
            .Select(x => x.Attempt)
            .DefaultIfEmpty(0)
            .Max() + 1;

        return Task.FromResult(nextAttempt);
    }
}