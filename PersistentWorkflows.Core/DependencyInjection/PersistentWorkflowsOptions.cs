using PersistentWorkflows.Core.Registry;

namespace PersistentWorkflows.Core.DependencyInjection;

public sealed class PersistentWorkflowsOptions
{
    internal List<WorkflowRegistration> Registrations { get; } = [];
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(1);
    public TimeSpan LeaseRenewalInterval { get; set; } = TimeSpan.FromSeconds(15);
    public TimeSpan StepTimeout { get; set; } = TimeSpan.FromMinutes(5);
    public int MaximumAttempts { get; set; } = 5;
    public TimeSpan InitialRetryDelay { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan MaximumRetryDelay { get; set; } = TimeSpan.FromMinutes(5);
    public double RetryJitterRatio { get; set; } = 0.2;
    public Func<Exception, bool> IsRetryableException { get; set; } = _ => true;
}
