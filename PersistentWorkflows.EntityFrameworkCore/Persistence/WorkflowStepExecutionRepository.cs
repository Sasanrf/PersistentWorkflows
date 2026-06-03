using Microsoft.EntityFrameworkCore;
using PersistentWorkflows.Abstractions.Persistence;
using PersistentWorkflows.EntityFrameworkCore.Entities;

namespace PersistentWorkflows.EntityFrameworkCore.Persistence;

internal sealed class WorkflowStepExecutionRepository : IWorkflowStepExecutionRepository
{
    private readonly PersistentWorkflowsDbContext _dbContext;

    public WorkflowStepExecutionRepository(PersistentWorkflowsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(WorkflowStepExecutionState stepExecution, CancellationToken cancellationToken)
    {
        var entity = new WorkflowStepExecutionEntity
        {
            Id = stepExecution.Id,
            WorkflowInstanceId = stepExecution.WorkflowInstanceId,
            StepName = stepExecution.StepName,
            Status = stepExecution.Status,
            Attempt = stepExecution.Attempt,
            StartedAtUtc = stepExecution.StartedAtUtc,
            CompletedAtUtc = stepExecution.CompletedAtUtc,
            ErrorCode = stepExecution.ErrorCode,
            ErrorMessage = stepExecution.ErrorMessage
        };

        _dbContext.WorkflowStepExecutions.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(
        WorkflowStepExecutionState stepExecution,
        CancellationToken cancellationToken)
    {
        var entity = await _dbContext.WorkflowStepExecutions
            .FirstAsync(x => x.Id == stepExecution.Id, cancellationToken);

        entity.Status = stepExecution.Status;
        entity.Attempt = stepExecution.Attempt;
        entity.CompletedAtUtc = stepExecution.CompletedAtUtc;
        entity.ErrorCode = stepExecution.ErrorCode;
        entity.ErrorMessage = stepExecution.ErrorMessage;

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> GetNextAttemptAsync(
        Guid workflowInstanceId,
        string stepName,
        CancellationToken cancellationToken)
    {
        var maxAttempt = await _dbContext.WorkflowStepExecutions
            .Where(x => x.WorkflowInstanceId == workflowInstanceId && x.StepName == stepName)
            .MaxAsync(x => (int?)x.Attempt, cancellationToken);

        return (maxAttempt ?? 0) + 1;
    }
}