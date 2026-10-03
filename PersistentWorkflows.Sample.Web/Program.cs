using Microsoft.Extensions.DependencyInjection;
using PersistentWorkflows.Abstractions.Definitions;
using PersistentWorkflows.Abstractions.Execution;
using PersistentWorkflows.Abstractions.Results;
using PersistentWorkflows.Core.DependencyInjection;
using PersistentWorkflows.EntityFrameworkCore.Persistence;
using PersistentWorkflows.EntityFrameworkCore.SqlServer;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
var connection = builder.Configuration.GetConnectionString("Workflows")
    ?? Environment.GetEnvironmentVariable("PERSISTENTWORKFLOWS_CONNECTION")
    ?? "Server=localhost\\SQLEXPRESS;Database=PersistentWorkflowsWebSample;Trusted_Connection=True;TrustServerCertificate=True;";
builder.Services.AddPersistentWorkflowsSqlServer(connection);
builder.Services.AddWorkflow<OrderWorkflow, OrderData>();
builder.Services.AddScoped<ValidateOrder>();
builder.Services.AddScoped<WaitForApproval>();
var app = builder.Build();
// For this local sample only. Deploy migrations separately in production.
await app.Services.EnsurePersistentWorkflowsDatabaseAsync();
app.MapPost("/orders/{businessKey}", async (string businessKey, OrderData data, IWorkflowRunner runner, CancellationToken ct) =>
{
    try
    {
        var result = await runner.EnqueueAsync<OrderWorkflow, OrderData>(businessKey, data, ct);
        return Results.Accepted($"/workflows/{result.WorkflowInstanceId}", result);
    }
    catch (WorkflowInputConflictException ex) { return Results.Conflict(new { ex.Message }); }
});
app.MapGet("/workflows/{id:guid}", async (Guid id, IWorkflowRunner runner, CancellationToken ct) =>
{
    var state = await runner.GetAsync(id, ct);
    return state is null ? Results.NotFound() : Results.Ok(new { state.Id, state.ExecutionStatus, state.CurrentStepName, state.NextExecutionAtUtc, state.LastErrorCode });
});
app.MapPost("/workflows/{id:guid}/approve", async (Guid id, IWorkflowRunner runner, CancellationToken ct) =>
{
    if (await runner.GetAsync(id, ct) is null) return Results.NotFound();
    return Results.Ok(new { Accepted = await runner.SignalAsync(id, "approval", cancellationToken: ct) });
});
app.MapPost("/workflows/{id:guid}/cancel", async (Guid id, IWorkflowRunner runner, CancellationToken ct) =>
    Results.Ok(new { Requested = await runner.CancelAsync(id, ct) }));
await app.RunAsync();

public sealed class OrderData { public decimal Amount { get; set; } public bool Validated { get; set; } }
public sealed class OrderWorkflow : IWorkflowDefinition<OrderData>
{
    public string Name => "order";
    public void Build(IWorkflowBuilder<OrderData> builder) => builder.Step<ValidateOrder>("validate").Step<WaitForApproval>("approval");
}
public sealed class ValidateOrder : IWorkflowStep<OrderData>
{
    public Task<StepResult> ExecuteAsync(WorkflowExecutionContext<OrderData> context, CancellationToken ct)
    {
        if (context.Data.Amount <= 0) return Task.FromResult(StepResult.Fail("InvalidAmount", "Amount must be positive."));
        context.Data.Validated = true;
        return Task.FromResult(StepResult.Success());
    }
}
public sealed class WaitForApproval : IWorkflowStep<OrderData>
{
    public async Task<StepResult> ExecuteAsync(WorkflowExecutionContext<OrderData> context, CancellationToken ct) =>
        await context.GetSignalAsync("approval", ct) is null ? StepResult.WaitForSignal("approval") : StepResult.Success();
}
