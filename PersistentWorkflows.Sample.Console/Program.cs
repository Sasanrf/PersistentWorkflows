using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PersistentWorkflows.Abstractions.Definitions;
using PersistentWorkflows.Abstractions.Execution;
using PersistentWorkflows.Abstractions.Results;
using PersistentWorkflows.Core.DependencyInjection;
using PersistentWorkflows.EntityFrameworkCore.SqlServer;
using PersistentWorkflows.EntityFrameworkCore.Persistence;

var builder = Host.CreateApplicationBuilder(args);
var connection = Environment.GetEnvironmentVariable("PERSISTENTWORKFLOWS_CONNECTION")
    ?? "Server=localhost\\SQLEXPRESS;Database=PersistentWorkflowsSample;Trusted_Connection=True;TrustServerCertificate=True;";
builder.Services.AddPersistentWorkflowsSqlServer(connection, configureWorker: o => o.PollingInterval = TimeSpan.FromSeconds(1));
builder.Services.AddWorkflow<CreateInvoiceWorkflow, CreateInvoiceContext>();
builder.Services.AddScoped<ValidateCustomerStep>();
builder.Services.AddScoped<WaitForExternalApprovalStep>();
builder.Services.AddScoped<CreateInvoiceStep>();
using var host = builder.Build();
await host.Services.EnsurePersistentWorkflowsDatabaseAsync();
await host.StartAsync();
try
{
    Guid id;
    await using (var scope = host.Services.CreateAsyncScope())
    {
        var runner = scope.ServiceProvider.GetRequiredService<IWorkflowRunner>();
        var result = await runner.EnqueueAsync<CreateInvoiceWorkflow, CreateInvoiceContext>(
            $"invoice-{Guid.NewGuid():N}", new() { ClientId = 100, InvoiceNumber = "INV-1001" });
        id = result.WorkflowInstanceId;
        Console.WriteLine($"Accepted workflow: {id} ({result.Status})");
    }
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    while (true)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<IWorkflowRunner>();
        var instance = (await runner.GetAsync(id, deadline.Token))!;
        if (instance.Status is "Succeeded" or "Failed" or "Cancelled")
        {
            Console.WriteLine($"Final status: {instance.Status}");
            foreach (var step in await runner.GetHistoryAsync(id, cancellationToken: deadline.Token))
                Console.WriteLine($"{step.StepName} | {step.Status} | Attempt {step.Attempt}");
            break;
        }
        await Task.Delay(TimeSpan.FromMilliseconds(250), deadline.Token);
    }
}
finally { await host.StopAsync(); }

public sealed class CreateInvoiceContext
{
    public int ClientId { get; set; }

    public string InvoiceNumber { get; set; } = string.Empty;

    public bool CustomerValidated { get; set; }

    public bool ApprovalWaitedOnce { get; set; }

    public bool InvoiceCreated { get; set; }
}

public sealed class CreateInvoiceWorkflow : IWorkflowDefinition<CreateInvoiceContext>
{
    public string Name => "create-invoice";

    public void Build(IWorkflowBuilder<CreateInvoiceContext> builder)
    {
        builder
            .Step<ValidateCustomerStep>("validate-customer")
            .Step<WaitForExternalApprovalStep>("wait-for-approval")
            .Step<CreateInvoiceStep>("create-invoice");
    }
}

public sealed class ValidateCustomerStep : IWorkflowStep<CreateInvoiceContext>
{
    public Task<StepResult> ExecuteAsync(
        WorkflowExecutionContext<CreateInvoiceContext> context,
        CancellationToken cancellationToken)
    {
        context.Data.CustomerValidated = true;

        Console.WriteLine("Customer validated.");

        return Task.FromResult(StepResult.Success());
    }
}

public sealed class WaitForExternalApprovalStep : IWorkflowStep<CreateInvoiceContext>
{
    public Task<StepResult> ExecuteAsync(
        WorkflowExecutionContext<CreateInvoiceContext> context,
        CancellationToken cancellationToken)
    {
        if (!context.Data.ApprovalWaitedOnce)
        {
            context.Data.ApprovalWaitedOnce = true;

            Console.WriteLine("Approval not ready. Waiting...");

            return Task.FromResult(StepResult.Wait(TimeSpan.FromSeconds(5)));
        }

        Console.WriteLine("Approval received.");

        return Task.FromResult(StepResult.Success());
    }
}

public sealed class CreateInvoiceStep : IWorkflowStep<CreateInvoiceContext>
{
    public Task<StepResult> ExecuteAsync(
        WorkflowExecutionContext<CreateInvoiceContext> context,
        CancellationToken cancellationToken)
    {
        context.Data.InvoiceCreated = true;

        Console.WriteLine($"Invoice created: {context.Data.InvoiceNumber}");

        return Task.FromResult(StepResult.Success());
    }
}
