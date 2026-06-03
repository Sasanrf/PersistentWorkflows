using Microsoft.EntityFrameworkCore;
using PersistentWorkflows.Abstractions.Persistence;
using PersistentWorkflows.EntityFrameworkCore.Entities;

namespace PersistentWorkflows.EntityFrameworkCore.Persistence;

internal sealed class WorkflowInstanceRepository : IWorkflowInstanceRepository
{
    private readonly PersistentWorkflowsDbContext _dbContext;

    public WorkflowInstanceRepository(PersistentWorkflowsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<WorkflowInstanceState?> GetByWorkflowNameAndInstanceKeyAsync(
        string workflowName,
        string instanceKey,
        CancellationToken cancellationToken)
    {
        var entity = await _dbContext.WorkflowInstances
            .FirstOrDefaultAsync(
                x => x.WorkflowName == workflowName && x.InstanceKey == instanceKey,
                cancellationToken);

        return entity is null ? null : Map(entity);
    }

    public async Task<WorkflowInstanceState?> GetByIdAsync(
        Guid workflowInstanceId,
        CancellationToken cancellationToken)
    {
        var entity = await _dbContext.WorkflowInstances
            .FirstOrDefaultAsync(x => x.Id == workflowInstanceId, cancellationToken);

        return entity is null ? null : Map(entity);
    }

    public async Task AddAsync(WorkflowInstanceState instance, CancellationToken cancellationToken)
    {
        var entity = new WorkflowInstanceEntity
        {
            Id = instance.Id,
            WorkflowName = instance.WorkflowName,
            InstanceKey = instance.InstanceKey,
            ContextJson = instance.ContextJson,
            Status = instance.Status,
            CurrentStepIndex = instance.CurrentStepIndex,
            CreatedAtUtc = instance.CreatedAtUtc,
            UpdatedAtUtc = instance.UpdatedAtUtc,
            NextExecutionAtUtc = instance.NextExecutionAtUtc,
            Version = instance.Version
        };

        _dbContext.WorkflowInstances.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(WorkflowInstanceState instance, CancellationToken cancellationToken)
    {
        var entity = await _dbContext.WorkflowInstances
            .FirstOrDefaultAsync(x => x.Id == instance.Id, cancellationToken);

        if (entity is null)
        {
            throw new InvalidOperationException(
                $"Workflow instance '{instance.Id}' was not found.");
        }

        var originalVersion = instance.Version;

        entity.ContextJson = instance.ContextJson;
        entity.Status = instance.Status;
        entity.CurrentStepIndex = instance.CurrentStepIndex;
        entity.UpdatedAtUtc = instance.UpdatedAtUtc;
        entity.NextExecutionAtUtc = instance.NextExecutionAtUtc;
        entity.Version = originalVersion + 1;

        _dbContext.Entry(entity).Property(x => x.Version).OriginalValue = originalVersion;

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            instance.Version = entity.Version;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new InvalidOperationException(
                $"Workflow instance '{instance.Id}' was updated by another process.",
                ex);
        }
    }

    private static WorkflowInstanceState Map(WorkflowInstanceEntity entity)
    {
        return new WorkflowInstanceState
        {
            Id = entity.Id,
            WorkflowName = entity.WorkflowName,
            InstanceKey = entity.InstanceKey,
            ContextJson = entity.ContextJson,
            Status = entity.Status,
            CurrentStepIndex = entity.CurrentStepIndex,
            CreatedAtUtc = entity.CreatedAtUtc,
            UpdatedAtUtc = entity.UpdatedAtUtc,
            NextExecutionAtUtc = entity.NextExecutionAtUtc,
            Version = entity.Version
        };
    }

    public async Task<IReadOnlyList<WorkflowInstanceState>> GetWaitingForResumeAsync(
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var entities = await _dbContext.WorkflowInstances
            .Where(x =>
                x.Status == "Waiting" &&
                x.NextExecutionAtUtc != null &&
                x.NextExecutionAtUtc <= utcNow)
            .OrderBy(x => x.NextExecutionAtUtc)
            .ToListAsync(cancellationToken);

        return entities.Select(Map).ToList();
    }
}