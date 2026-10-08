using Microsoft.Extensions.DependencyInjection;
using PersistentWorkflows.Abstractions.Definitions;
using PersistentWorkflows.Abstractions.Execution;
using PersistentWorkflows.Abstractions.Results;
using PersistentWorkflows.Core.DependencyInjection;
using PersistentWorkflows.EntityFrameworkCore.SqlServer;

namespace FailureWorker;

public static class WorkerSetup
{
    public static void Add(IServiceCollection services, string connection, StepControl control)
    {
        services.AddPersistentWorkflowsSqlServer(connection,
            configureExecution: o => { o.LeaseDuration = TimeSpan.FromSeconds(3); o.LeaseRenewalInterval = TimeSpan.FromMilliseconds(200); },
            configureWorker: o => { o.PollingInterval = TimeSpan.FromMilliseconds(50); o.MaximumConcurrency = 1; o.BatchSize = 1; });
        services.AddWorkflow<Sequence, Payload>();
        services.AddScoped<First>();
        services.AddScoped<Second>();
        services.AddSingleton(control);
    }
}

public sealed class Payload { public List<string> Trace { get; set; } = []; }
public sealed class StepControl
{
    public Func<WorkflowExecutionContext<Payload>, CancellationToken, Task> BeforeSecond { get; init; } = (_, _) => Task.CompletedTask;
}
public sealed class Sequence : IWorkflowDefinition<Payload>
{
    public string Name => "failure-worker";
    public void Build(IWorkflowBuilder<Payload> builder) { builder.Step<First>("first"); builder.Step<Second>("second"); }
}
public sealed class First : IWorkflowStep<Payload>
{
    public Task<StepResult> ExecuteAsync(WorkflowExecutionContext<Payload> context, CancellationToken ct)
    {
        if (context.Data.Trace.Count != 0) throw new InvalidOperationException("First step replayed committed context.");
        context.Data.Trace.Add("first");
        return Task.FromResult(StepResult.Success());
    }
}
public sealed class Second(StepControl control) : IWorkflowStep<Payload>
{
    public async Task<StepResult> ExecuteAsync(WorkflowExecutionContext<Payload> context, CancellationToken ct)
    {
        if (!context.Data.Trace.SequenceEqual(["first"])) throw new InvalidOperationException("Incorrect checkpoint context.");
        await control.BeforeSecond(context, ct);
        context.Data.Trace.Add("second");
        return StepResult.Success();
    }
}
