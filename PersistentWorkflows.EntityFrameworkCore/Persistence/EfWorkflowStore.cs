using Microsoft.EntityFrameworkCore;
using PersistentWorkflows.Abstractions.Execution;
using PersistentWorkflows.Abstractions.Persistence;
using PersistentWorkflows.EntityFrameworkCore.Entities;

namespace PersistentWorkflows.EntityFrameworkCore.Persistence;

/// <summary>Uses a fresh context for each operation so heartbeat and execution never share a DbContext.</summary>
internal sealed class EfWorkflowStore(DbContextOptions<PersistentWorkflowsDbContext> options) : IWorkflowStore
{
    private PersistentWorkflowsDbContext Open() => new(options);
    private static IQueryable<WorkflowInstanceEntity> Due(PersistentWorkflowsDbContext db, DateTime now) => db.WorkflowInstances.Where(x =>
        x.Status == "Pending" ||
        (x.Status == "Running" && (x.LeaseExpiresAtUtc == null || x.LeaseExpiresAtUtc <= now)) ||
        (x.Status == "Waiting" && (x.CancellationRequested || x.NextExecutionAtUtc <= now ||
            (x.WaitingSignal != null && db.WorkflowSignals.Any(s => s.WorkflowInstanceId == x.Id && s.Name == x.WaitingSignal)))));
    private static IQueryable<WorkflowInstanceEntity> Owned(PersistentWorkflowsDbContext db, Guid id, Guid token, DateTime now) =>
        db.WorkflowInstances.Where(x => x.Id == id && x.Status == "Running" && x.LeaseToken == token && x.LeaseExpiresAtUtc > now);

    public async Task<(WorkflowInstanceState Instance, bool Created)> CreateAsync(WorkflowInstanceState instance, CancellationToken ct)
    {
        await using var db = Open();
        var existing = await db.WorkflowInstances.AsNoTracking().SingleOrDefaultAsync(x => x.WorkflowName == instance.WorkflowName && x.InstanceKey == instance.InstanceKey, ct);
        if (existing is not null) return (Map(existing), false);
        var entity = ToEntity(instance);
        db.Add(entity);
        try { await db.SaveChangesAsync(ct); return (Map(entity), true); }
        catch (DbUpdateException ex) when (IsUnique(ex))
        {
            db.Entry(entity).State = EntityState.Detached;
            existing = await db.WorkflowInstances.AsNoTracking().SingleOrDefaultAsync(x => x.WorkflowName == instance.WorkflowName && x.InstanceKey == instance.InstanceKey, ct);
            if (existing is null) throw;
            return (Map(existing), false);
        }
    }
    private static bool IsUnique(Exception ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (current.GetType().FullName == "Microsoft.Data.SqlClient.SqlException" &&
                current.GetType().GetProperty("Number")?.GetValue(current) is int number && number is 2601 or 2627) return true;
            // SQLite remains optional: inspect its structured error code without a hard provider dependency.
            if (current.GetType().FullName == "Microsoft.Data.Sqlite.SqliteException" &&
                current.GetType().GetProperty("SqliteExtendedErrorCode")?.GetValue(current) is int code && code is 1555 or 2067) return true;
        }
        return false;
    }
    public async Task<WorkflowInstanceState?> GetAsync(Guid id, CancellationToken ct)
    {
        await using var db = Open();
        var entity = await db.WorkflowInstances.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        return entity is null ? null : Map(entity);
    }
    public async Task<IReadOnlyList<Guid>> GetDueAsync(DateTime now, int limit, CancellationToken ct)
    {
        await using var db = Open();
        return await Due(db, now).OrderBy(x => x.NextExecutionAtUtc ?? x.CreatedAtUtc).ThenBy(x => x.Id).Take(limit).Select(x => x.Id).ToListAsync(ct);
    }
    public async Task<WorkflowInstanceState?> TryClaimAsync(Guid id, Guid token, DateTime now, TimeSpan duration, bool force, CancellationToken ct)
    {
        await using var db = Open();
        var candidates = force ? db.WorkflowInstances.Where(x => x.Status == "Pending" || x.Status == "Waiting" ||
            (x.Status == "Running" && (x.LeaseExpiresAtUtc == null || x.LeaseExpiresAtUtc <= now))) : Due(db, now);
        var count = await candidates.Where(x => x.Id == id).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.Status, "Running").SetProperty(x => x.LeaseToken, (Guid?)token)
            .SetProperty(x => x.LeaseExpiresAtUtc, (DateTime?)now.Add(duration))
            .SetProperty(x => x.UpdatedAtUtc, now).SetProperty(x => x.Version, x => x.Version + 1), ct);
        return count == 0 ? null : await GetAsync(id, ct);
    }
    public async Task<bool> RenewLeaseAsync(Guid id, Guid token, DateTime now, TimeSpan duration, CancellationToken ct)
    {
        await using var db = Open();
        return await Owned(db, id, token, now).Where(x => !x.CancellationRequested).ExecuteUpdateAsync(s => s.SetProperty(x => x.LeaseExpiresAtUtc, (DateTime?)now.Add(duration)), ct) == 1;
    }
    public async Task<WorkflowStepExecutionState> BeginStepAsync(Guid id, Guid token, string stepName, DateTime now, CancellationToken ct)
    {
        await using var db = Open();
        var executionId = Guid.NewGuid();
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            if (await Owned(db, id, token, now).Where(x => !x.CancellationRequested).ExecuteUpdateAsync(s => s.SetProperty(x => x.Version, x => x.Version + 1), ct) != 1)
                throw new WorkflowLeaseLostException();
            var existing = await db.WorkflowStepExecutions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == executionId, ct);
            if (existing is not null) { await tx.CommitAsync(ct); return Map(existing); }
            await db.WorkflowStepExecutions.Where(x => x.WorkflowInstanceId == id && x.Status == "Started")
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "Abandoned").SetProperty(x => x.CompletedAtUtc, (DateTime?)now)
                    .SetProperty(x => x.ErrorCode, "ExecutionInterrupted"), ct);
            var attempt = (await db.WorkflowStepExecutions.Where(x => x.WorkflowInstanceId == id && x.StepName == stepName).MaxAsync(x => (int?)x.Attempt, ct) ?? 0) + 1;
            var entity = new WorkflowStepExecutionEntity { Id = executionId, WorkflowInstanceId = id, StepName = stepName, Status = "Started", Attempt = attempt, StartedAtUtc = now };
            db.Add(entity);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            db.Entry(entity).State = EntityState.Detached;
            return Map(entity);
        });
    }
    public async Task<bool> CommitAsync(WorkflowInstanceState state, Guid token, WorkflowStepExecutionState? execution, DateTime now, CancellationToken ct)
    {
        await using var db = Open();
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            // A connection can fail after COMMIT succeeds. A repeated acknowledgement must not
            // reapply progress or reject an already durable outcome under the same attempt ID.
            if (execution is not null)
            {
                var completed = await db.WorkflowStepExecutions.AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Id == execution.Id && x.WorkflowInstanceId == state.Id && x.Status != "Started", ct);
                if (completed is not null)
                    return completed.Status == execution.Status && completed.CompletedAtUtc == execution.CompletedAtUtc &&
                        completed.ErrorCode == execution.ErrorCode && completed.ErrorMessage == execution.ErrorMessage;
            }
            var count = await Owned(db, state.Id, token, now).ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, x => x.CancellationRequested ? "Cancelled" : state.Status)
                .SetProperty(x => x.ContextJson, state.ContextJson).SetProperty(x => x.ContextSchemaVersion, state.ContextSchemaVersion)
                .SetProperty(x => x.DefinitionHash, state.DefinitionHash).SetProperty(x => x.CurrentStepIndex, state.CurrentStepIndex)
                .SetProperty(x => x.CurrentStepName, state.CurrentStepName).SetProperty(x => x.UpdatedAtUtc, now)
                .SetProperty(x => x.NextExecutionAtUtc, state.NextExecutionAtUtc)
                .SetProperty(x => x.WaitingSignal, state.WaitingSignal)
                .SetProperty(x => x.ConsecutiveFailures, state.ConsecutiveFailures)
                .SetProperty(x => x.LastErrorCode, state.LastErrorCode).SetProperty(x => x.LastErrorMessage, state.LastErrorMessage)
                .SetProperty(x => x.LeaseToken, x => x.CancellationRequested || state.Status != "Running" ? null : x.LeaseToken)
                .SetProperty(x => x.LeaseExpiresAtUtc, x => x.CancellationRequested || state.Status != "Running" ? null : x.LeaseExpiresAtUtc)
                .SetProperty(x => x.Version, x => x.Version + 1), ct);
            if (count == 0) return false;
            await db.WorkflowInstances.Where(x => x.Id == state.Id && x.CancellationRequested)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.NextExecutionAtUtc, (DateTime?)null).SetProperty(x => x.WaitingSignal, (string?)null), ct);
            if (execution is not null)
            {
                if (await db.WorkflowStepExecutions.Where(x => x.Id == execution.Id && x.WorkflowInstanceId == state.Id && x.Status == "Started")
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, execution.Status).SetProperty(x => x.CompletedAtUtc, execution.CompletedAtUtc)
                        .SetProperty(x => x.ErrorCode, execution.ErrorCode).SetProperty(x => x.ErrorMessage, execution.ErrorMessage), ct) != 1)
                    throw new InvalidOperationException("The current step execution was not found.");
            }
            await tx.CommitAsync(ct);
            return true;
        });
    }
    public async Task<bool> ReleaseAsync(Guid id, Guid token, DateTime now, CancellationToken ct)
    {
        await using var db = Open();
        return await Owned(db, id, token, now).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.Status, x => x.CancellationRequested ? "Cancelled" : "Pending")
            .SetProperty(x => x.LeaseToken, (Guid?)null).SetProperty(x => x.LeaseExpiresAtUtc, (DateTime?)null)
            .SetProperty(x => x.UpdatedAtUtc, now).SetProperty(x => x.Version, x => x.Version + 1), ct) == 1;
    }
    public async Task<bool> RequestCancellationAsync(Guid id, DateTime now, CancellationToken ct)
    {
        await using var db = Open();
        return await db.WorkflowInstances.Where(x => x.Id == id && (x.Status == "Pending" || x.Status == "Running" || x.Status == "Waiting"))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.CancellationRequested, true).SetProperty(x => x.UpdatedAtUtc, now)
                .SetProperty(x => x.Status, x => x.Status == "Running" ? "Running" : "Cancelled")
                .SetProperty(x => x.Version, x => x.Version + 1), ct) == 1;
    }
    public async Task<bool> RetryAsync(Guid id, string reason, DateTime now, CancellationToken ct)
    {
        await using var db = Open();
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var count = await db.WorkflowInstances.Where(x => x.Id == id && x.Status == "Failed").ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, "Pending").SetProperty(x => x.ConsecutiveFailures, 0)
                .SetProperty(x => x.CancellationRequested, false).SetProperty(x => x.LastErrorCode, (string?)null)
                .SetProperty(x => x.LastErrorMessage, (string?)null).SetProperty(x => x.UpdatedAtUtc, now)
                .SetProperty(x => x.Version, x => x.Version + 1), ct);
            if (count == 0) return false;
            var name = "$manual-retry";
            var attempt = (await db.WorkflowStepExecutions.Where(x => x.WorkflowInstanceId == id && x.StepName == name).MaxAsync(x => (int?)x.Attempt, ct) ?? 0) + 1;
            db.Add(new WorkflowStepExecutionEntity { Id = Guid.NewGuid(), WorkflowInstanceId = id, StepName = name, Attempt = attempt, Status = "ManualRetry", StartedAtUtc = now, CompletedAtUtc = now, ErrorMessage = reason });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            db.ChangeTracker.Clear();
            return true;
        });
    }
    public async Task<bool> SignalAsync(Guid id, string name, string? payload, DateTime now, CancellationToken ct)
    {
        await using var db = Open();
        if (!await db.WorkflowInstances.AnyAsync(x => x.Id == id, ct)) throw new InvalidOperationException($"Workflow instance '{id}' was not found.");
        db.Add(new WorkflowSignalEntity { WorkflowInstanceId = id, Name = name, PayloadJson = payload, ReceivedAtUtc = now });
        try { await db.SaveChangesAsync(ct); return true; }
        catch (DbUpdateException ex) when (IsUnique(ex)) { return false; }
    }
    public async Task<WorkflowSignalState?> GetSignalAsync(Guid id, string name, CancellationToken ct)
    {
        await using var db = Open();
        return await db.WorkflowSignals.Where(x => x.WorkflowInstanceId == id && x.Name == name)
            .Select(x => new WorkflowSignalState(x.Name, x.PayloadJson, x.ReceivedAtUtc)).SingleOrDefaultAsync(ct);
    }
    public async Task<IReadOnlyList<WorkflowInstanceState>> ListAsync(string? status, int skip, int take, CancellationToken ct)
    {
        await using var db = Open();
        var query = db.WorkflowInstances.AsNoTracking();
        if (status is not null) query = query.Where(x => x.Status == status);
        return (await query.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id).Skip(skip).Take(take).ToListAsync(ct)).Select(Map).ToList();
    }
    public async Task<IReadOnlyList<WorkflowStepExecutionState>> GetHistoryAsync(Guid id, int skip, int take, CancellationToken ct)
    {
        await using var db = Open();
        return (await db.WorkflowStepExecutions.AsNoTracking().Where(x => x.WorkflowInstanceId == id).OrderBy(x => x.StartedAtUtc).ThenBy(x => x.Attempt).ThenBy(x => x.Id).Skip(skip).Take(take).ToListAsync(ct)).Select(Map).ToList();
    }
    public async Task<int> DeleteCompletedAsync(DateTime before, int limit, CancellationToken ct)
    {
        await using var db = Open();
        var ids = db.WorkflowInstances.Where(x => (x.Status == "Succeeded" || x.Status == "Cancelled") && x.UpdatedAtUtc < before)
            .OrderBy(x => x.UpdatedAtUtc).Take(limit).Select(x => x.Id);
        return await db.WorkflowInstances.Where(x => ids.Contains(x.Id)).ExecuteDeleteAsync(ct);
    }
    internal static WorkflowInstanceState Map(WorkflowInstanceEntity x) => new()
    {
        Id = x.Id, WorkflowName = x.WorkflowName, InstanceKey = x.InstanceKey, ContextJson = x.ContextJson, Status = x.Status,
        CurrentStepIndex = x.CurrentStepIndex, CreatedAtUtc = x.CreatedAtUtc, UpdatedAtUtc = x.UpdatedAtUtc, NextExecutionAtUtc = x.NextExecutionAtUtc, Version = x.Version,
        DefinitionVersion = x.DefinitionVersion, ContextSchemaVersion = x.ContextSchemaVersion, InputHash = x.InputHash, DefinitionHash = x.DefinitionHash,
        CurrentStepName = x.CurrentStepName, LeaseToken = x.LeaseToken, LeaseExpiresAtUtc = x.LeaseExpiresAtUtc, CancellationRequested = x.CancellationRequested,
        ConsecutiveFailures = x.ConsecutiveFailures, WaitingSignal = x.WaitingSignal, LastErrorCode = x.LastErrorCode, LastErrorMessage = x.LastErrorMessage
    };
    private static WorkflowInstanceEntity ToEntity(WorkflowInstanceState x) => new()
    {
        Id = x.Id, WorkflowName = x.WorkflowName, InstanceKey = x.InstanceKey, ContextJson = x.ContextJson, Status = x.Status,
        CurrentStepIndex = x.CurrentStepIndex, CreatedAtUtc = x.CreatedAtUtc, UpdatedAtUtc = x.UpdatedAtUtc, NextExecutionAtUtc = x.NextExecutionAtUtc, Version = x.Version,
        DefinitionVersion = x.DefinitionVersion, ContextSchemaVersion = x.ContextSchemaVersion, InputHash = x.InputHash, DefinitionHash = x.DefinitionHash, CurrentStepName = x.CurrentStepName
    };
    private static WorkflowStepExecutionState Map(WorkflowStepExecutionEntity x) => new()
    {
        Id = x.Id, WorkflowInstanceId = x.WorkflowInstanceId, StepName = x.StepName, Attempt = x.Attempt, Status = x.Status,
        StartedAtUtc = x.StartedAtUtc, CompletedAtUtc = x.CompletedAtUtc, ErrorCode = x.ErrorCode, ErrorMessage = x.ErrorMessage
    };
}
