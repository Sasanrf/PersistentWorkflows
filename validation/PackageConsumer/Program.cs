using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PersistentWorkflows.Abstractions.Definitions;
using PersistentWorkflows.Abstractions.Execution;
using PersistentWorkflows.Abstractions.Results;
using PersistentWorkflows.Core.DependencyInjection;
using PersistentWorkflows.EntityFrameworkCore.Persistence;
using PersistentWorkflows.EntityFrameworkCore.SqlServer;

var server = Environment.GetEnvironmentVariable("PERSISTENTWORKFLOWS_TEST_SQLSERVER")
    ?? throw new InvalidOperationException("Set PERSISTENTWORKFLOWS_TEST_SQLSERVER to run the package smoke test.");
var database = $"PersistentWorkflowsPackageSmoke_{Guid.NewGuid():N}";
var connection = new SqlConnectionStringBuilder(server) { InitialCatalog = database }.ConnectionString;
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddPersistentWorkflowsSqlServer(connection, configureWorker: o => o.PollingInterval = TimeSpan.FromMilliseconds(25));
builder.Services.AddWorkflow<SmokeWorkflow, SmokeData>();
builder.Services.AddScoped<FirstAction>();
builder.Services.AddScoped<SecondAction>();
using var host = builder.Build();
await host.Services.EnsurePersistentWorkflowsDatabaseAsync();
try
{
    await host.StartAsync();
    Guid id;
    await using (var scope = host.Services.CreateAsyncScope())
        id = (await scope.ServiceProvider.GetRequiredService<IWorkflowRunner>().EnqueueAsync<SmokeWorkflow, SmokeData>("smoke", new())).WorkflowInstanceId;
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
    while (true)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<IWorkflowRunner>();
        var state = (await runner.GetAsync(id, deadline.Token))!;
        if (state.Status is "Failed" or "Cancelled") throw new InvalidOperationException($"Smoke workflow ended as {state.Status}.");
        if (state.Status == "Succeeded")
        {
            var attempts = await runner.GetHistoryAsync(id, cancellationToken: deadline.Token);
            if (attempts.Count != 3 || !state.ContextJson.Contains("\"count\":2")) throw new InvalidOperationException("Smoke workflow did not execute both actions and retry.");
            Console.WriteLine("Single-package smoke passed: Succeeded; 3 attempts; 2 actions.");
            break;
        }
        await Task.Delay(25, deadline.Token);
    }
}
finally
{
    await host.StopAsync();
    await using var scope = host.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<PersistentWorkflowsDbContext>();
    var actual = new SqlConnectionStringBuilder(db.Database.GetConnectionString()).InitialCatalog;
    if (actual != database || !actual.StartsWith("PersistentWorkflowsPackageSmoke_", StringComparison.Ordinal)) throw new InvalidOperationException("Unexpected cleanup target.");
    await db.Database.EnsureDeletedAsync();
}

public sealed class SmokeData { public int Count { get; set; } }
public sealed class SmokeWorkflow : IWorkflowDefinition<SmokeData>
{
    public string Name => "package-smoke";
    public void Build(IWorkflowBuilder<SmokeData> builder) => builder.Step<FirstAction>("first").Step<SecondAction>("second");
}
public sealed class FirstAction : IWorkflowStep<SmokeData>
{
    public Task<StepResult> ExecuteAsync(WorkflowExecutionContext<SmokeData> context, CancellationToken ct)
    { context.Data.Count++; return Task.FromResult(StepResult.Success()); }
}
public sealed class SecondAction : IWorkflowStep<SmokeData>
{
    public Task<StepResult> ExecuteAsync(WorkflowExecutionContext<SmokeData> context, CancellationToken ct)
    {
        if (context.Attempt == 1) return Task.FromResult(StepResult.Retry("Temporary dependency failure", TimeSpan.FromMilliseconds(10)));
        context.Data.Count++;
        return Task.FromResult(StepResult.Success());
    }
}
