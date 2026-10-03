namespace PersistentWorkflows.Core.DependencyInjection;

public sealed class PersistentWorkflowsWorkerOptions
{
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(30);

    public int BatchSize { get; set; } = 100;
    public int MaximumConcurrency { get; set; } = 1;
}
