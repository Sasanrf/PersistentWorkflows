using PersistentWorkflows.Abstractions.Persistence;

namespace PersistentWorkflows.Core.Tests.TestDoubles;

public sealed class FakeWorkflowInstanceRepository : IWorkflowInstanceRepository
{
    private readonly List<WorkflowInstanceState> _instances = [];

    public IReadOnlyList<WorkflowInstanceState> Instances => _instances;

    public Task<WorkflowInstanceState?> GetByWorkflowNameAndInstanceKeyAsync(
        string workflowName,
        string instanceKey,
        CancellationToken cancellationToken)
    {
        var instance = _instances.FirstOrDefault(x =>
            x.WorkflowName == workflowName &&
            x.InstanceKey == instanceKey);

        return Task.FromResult(instance);
    }

    public Task<WorkflowInstanceState?> GetByIdAsync(
        Guid workflowInstanceId,
        CancellationToken cancellationToken)
    {
        var instance = _instances.FirstOrDefault(x => x.Id == workflowInstanceId);
        return Task.FromResult(instance);
    }

    public Task AddAsync(
        WorkflowInstanceState instance,
        CancellationToken cancellationToken)
    {
        _instances.Add(instance);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(
        WorkflowInstanceState instance,
        CancellationToken cancellationToken)
    {
        var existing = _instances.First(x => x.Id == instance.Id);

        existing.ContextJson = instance.ContextJson;
        existing.Status = instance.Status;
        existing.CurrentStepIndex = instance.CurrentStepIndex;
        existing.UpdatedAtUtc = instance.UpdatedAtUtc;
        existing.NextExecutionAtUtc = instance.NextExecutionAtUtc;
        existing.Version = instance.Version;

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<WorkflowInstanceState>> GetWaitingForResumeAsync(
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var result = _instances
            .Where(x =>
                x.Status == "Waiting" &&
                x.NextExecutionAtUtc is not null &&
                x.NextExecutionAtUtc <= utcNow)
            .OrderBy(x => x.NextExecutionAtUtc)
            .ToList();

        return Task.FromResult<IReadOnlyList<WorkflowInstanceState>>(result);
    }
}