using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace PersistentWorkflows.Core.Execution;

public static class WorkflowDiagnostics
{
    public const string Name = "PersistentWorkflows";
    public static readonly ActivitySource ActivitySource = new(Name);
    public static readonly Meter Meter = new(Name);
    internal static readonly Counter<long> Accepted = Meter.CreateCounter<long>("workflow.accepted");
    internal static readonly Counter<long> Outcomes = Meter.CreateCounter<long>("workflow.outcomes");
    internal static readonly Counter<long> Retries = Meter.CreateCounter<long>("workflow.retries");
    internal static readonly Counter<long> LeaseLosses = Meter.CreateCounter<long>("workflow.lease_losses");
    internal static readonly Histogram<double> StepDuration = Meter.CreateHistogram<double>("workflow.step.duration", "ms");
}
