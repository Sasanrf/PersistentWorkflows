# PersistentWorkflows

Durable, resumable workflows for .NET applications.

PersistentWorkflows helps you run multi-step business processes safely across application restarts, retries, failures, and distributed environments.

Examples:
- invoice processing
- payment flows
- customer onboarding
- file processing
- integrations with external systems
- long-running background operations

---

# Features

- Durable workflow execution
- Persisted workflow state
- Step-by-step execution history
- Automatic resume support
- Waiting and retry scheduling
- Idempotent workflow start
- Optimistic concurrency protection
- Background workflow resumer
- EF Core persistence provider
- Code-first workflow definitions
- Distributed-safe execution model

---

# Installation

```bash
dotnet add package PersistentWorkflows
dotnet add package PersistentWorkflows.EntityFrameworkCore
```

---

# Registration

```csharp
builder.Services.AddPersistentWorkflows();

builder.Services.AddPersistentWorkflowsEntityFrameworkCore(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddPersistentWorkflowsBackgroundResumer(options =>
{
    options.PollingInterval = TimeSpan.FromSeconds(15);
    options.BatchSize = 100;
});

builder.Services.AddWorkflow<CreateInvoiceWorkflow, CreateInvoiceContext>();
```

---

# Define a workflow

```csharp
public sealed class CreateInvoiceWorkflow
    : IWorkflowDefinition<CreateInvoiceContext>
{
    public string Name => "create-invoice";

    public void Build(IWorkflowBuilder<CreateInvoiceContext> builder)
    {
        builder
            .Step<ValidateCustomerStep>("validate-customer")
            .Step<CreateCustomerStep>("create-customer")
            .Step<CreateInvoiceStep>("create-invoice");
    }
}
```

---

# Define workflow context

```csharp
public sealed class CreateInvoiceContext
{
    public int ClientId { get; set; }

    public string InvoiceNumber { get; set; } = string.Empty;

    public Guid? CustomerId { get; set; }
}
```

---

# Define a workflow step

```csharp
public sealed class CreateCustomerStep
    : IWorkflowStep<CreateInvoiceContext>
{
    public async Task<StepResult> ExecuteAsync(
        WorkflowExecutionContext<CreateInvoiceContext> context,
        CancellationToken cancellationToken)
    {
        if (context.Data.CustomerId.HasValue)
        {
            return StepResult.Success();
        }

        var customerId = await CreateCustomer(cancellationToken);

        context.Data.CustomerId = customerId;

        return StepResult.Success();
    }

    private static Task<Guid> CreateCustomer(CancellationToken cancellationToken)
    {
        return Task.FromResult(Guid.NewGuid());
    }
}
```

---

# Start a workflow

```csharp
await workflowRunner.StartAsync<CreateInvoiceWorkflow, CreateInvoiceContext>(
    instanceKey: $"invoice-{invoiceId}",
    context: new CreateInvoiceContext
    {
        ClientId = 100,
        InvoiceNumber = "INV-1001"
    });
```

---

# Wait and retry

Steps can pause execution and resume later.

```csharp
public sealed class WaitForPaymentStep
    : IWorkflowStep<PaymentContext>
{
    public Task<StepResult> ExecuteAsync(
        WorkflowExecutionContext<PaymentContext> context,
        CancellationToken cancellationToken)
    {
        var paymentReceived = CheckPayment();

        if (!paymentReceived)
        {
            return Task.FromResult(
                StepResult.Wait(TimeSpan.FromMinutes(5)));
        }

        return Task.FromResult(StepResult.Success());
    }

    private static bool CheckPayment()
    {
        return false;
    }
}
```

---

# Resume workflows manually

```csharp
await workflowRunner.ResumeAsync(workflowInstanceId);
```

---

# Automatic background resume

```csharp
builder.Services.AddPersistentWorkflowsBackgroundResumer(options =>
{
    options.PollingInterval = TimeSpan.FromSeconds(30);
    options.BatchSize = 100;
});
```

---

# Execution guarantees

PersistentWorkflows guarantees:

- Persisted workflow progress
- Persisted workflow step history
- Sequential step execution
- Idempotent workflow creation by workflow name + instance key
- Optimistic concurrency protection
- Safe workflow resume after application restart

---

# Important limitations

PersistentWorkflows does NOT guarantee:

- Distributed transactions
- Exactly-once external API calls
- Automatic compensation/rollback
- Automatic idempotency inside workflow steps

Workflow steps should be designed to be idempotent whenever possible.

---

# Recommended usage

PersistentWorkflows works best for:

- Long-running business processes
- Retryable external integrations
- Background processing
- Durable orchestration
- Distributed application workflows

---

# Architecture

```text
PersistentWorkflows.Abstractions
PersistentWorkflows.Core
PersistentWorkflows.EntityFrameworkCore
```

Additional providers and integrations can be added independently.

---

# Current status

Current implementation includes:

- Workflow engine
- EF Core persistence provider
- Background workflow resumer
- SQLite integration tests
- Optimistic concurrency handling

Future improvements may include:

- OpenTelemetry integration
- Retry policies
- Workflow querying APIs
- Saga compensation support
- Additional persistence providers
- Dashboard/monitoring support