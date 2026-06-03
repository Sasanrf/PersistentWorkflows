using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PersistentWorkflows.Abstractions.Definitions;
using PersistentWorkflows.Abstractions.Execution;
using PersistentWorkflows.Abstractions.Results;
using PersistentWorkflows.Core.DependencyInjection;
using PersistentWorkflows.Core.Execution;
using PersistentWorkflows.EntityFrameworkCore;
using PersistentWorkflows.EntityFrameworkCore.Persistence;
using Microsoft.EntityFrameworkCore.SqlServer;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Services.AddPersistentWorkflows();

builder.Services.AddPersistentWorkflowsEntityFrameworkCore(options =>
{
    options.UseSqlServer(
        "Server=localhost;Database=Parking;Trusted_Connection=True;TrustServerCertificate=True;");
});

builder.Services.AddPersistentWorkflowsBackgroundResumer(options =>
{
    options.PollingInterval = TimeSpan.FromSeconds(5);
    options.BatchSize = 10;
});

builder.Services.AddWorkflow<CreateInvoiceWorkflow, CreateInvoiceContext>();

builder.Services.AddScoped<ValidateCustomerStep>();
builder.Services.AddScoped<WaitForExternalApprovalStep>();
builder.Services.AddScoped<CreateInvoiceStep>();

using var host = builder.Build();

await host.Services.EnsurePersistentWorkflowsDatabaseAsync();

Guid workflowInstanceId;

using (var scope = host.Services.CreateScope())
{
    var runner = scope.ServiceProvider.GetRequiredService<IWorkflowRunner>();

    var result = await runner.StartAsync<CreateInvoiceWorkflow, CreateInvoiceContext>(
        instanceKey: $"invoice-{Guid.NewGuid()}",
        context: new CreateInvoiceContext
        {
            ClientId = 100,
            InvoiceNumber = "INV-1001"
        });
    workflowInstanceId = result.WorkflowInstanceId;
    Console.WriteLine($"Started workflow: {result.WorkflowInstanceId}");
    Console.WriteLine($"Already exists: {result.AlreadyExists}");
}

using (var scope = host.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<PersistentWorkflowsDbContext>();

    var instance = await dbContext.WorkflowInstances
        .SingleAsync(x => x.Id == workflowInstanceId);

    Console.WriteLine();
    Console.WriteLine("After start:");
    Console.WriteLine($"Status: {instance.Status}");
    Console.WriteLine($"Current step index: {instance.CurrentStepIndex}");

    instance.NextExecutionAtUtc = DateTime.UtcNow.AddSeconds(-1);
    await dbContext.SaveChangesAsync();
}

using (var scope = host.Services.CreateScope())
{
    var runner = scope.ServiceProvider.GetRequiredService<IWorkflowRunner>();
    var dbContext = scope.ServiceProvider.GetRequiredService<PersistentWorkflowsDbContext>();

    var instance = await dbContext.WorkflowInstances
        .SingleAsync(x => x.Id == workflowInstanceId);

    await runner.ResumeAsync(instance.Id, CancellationToken.None);
}

using (var scope = host.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<PersistentWorkflowsDbContext>();

    var instance = await dbContext.WorkflowInstances
        .SingleAsync(x => x.Id == workflowInstanceId);
    var steps = await dbContext.WorkflowStepExecutions
        .Where(x => x.WorkflowInstanceId == workflowInstanceId)
        .OrderBy(x => x.StartedAtUtc)
        .ThenBy(x => x.Attempt)
        .ToListAsync();

    Console.WriteLine();
    Console.WriteLine("After resume:");
    Console.WriteLine($"Status: {instance.Status}");
    Console.WriteLine($"Current step index: {instance.CurrentStepIndex}");

    Console.WriteLine();
    Console.WriteLine("Step executions:");

    foreach (var step in steps)
    {
        Console.WriteLine(
            $"{step.StepName} | {step.Status} | Attempt {step.Attempt}");
    }
}

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