# PersistentWorkflows

Durable sequential workflows for .NET 10 applications. Applications define actions; the engine persists unfinished work, schedules retries, and recovers abandoned execution.

Version `0.1.0-preview.2` is a preview. SQL Server is the packaged database provider. The relational store also has SQLite conformance tests; SQLite migrations and a standalone SQLite installation package are not shipped yet.

## Install one package

```sh
dotnet add package PersistentWorkflows.EntityFrameworkCore.SqlServer --version 0.1.0-preview.2
```

NuGet brings in Core, Abstractions, and the EF store transitively. Consumers do not need four installation commands. Locally built packages can be installed from `artifacts/packages` before public publication.

## Register and define work

Create a .NET 10 console application, install the package above, and replace `Program.cs` with this example. Set `PERSISTENTWORKFLOWS_CONNECTION` to your SQL Server connection string before running it. The example applies migrations for local development, queues an order, records its approval, and runs the worker until you stop the application.

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PersistentWorkflows.Abstractions.Definitions;
using PersistentWorkflows.Abstractions.Execution;
using PersistentWorkflows.Abstractions.Results;
using PersistentWorkflows.Core.DependencyInjection;
using PersistentWorkflows.EntityFrameworkCore.SqlServer;
using PersistentWorkflows.EntityFrameworkCore.Persistence;

var builder = Host.CreateApplicationBuilder(args);
var connectionString = Environment.GetEnvironmentVariable("PERSISTENTWORKFLOWS_CONNECTION")
    ?? throw new InvalidOperationException("Set PERSISTENTWORKFLOWS_CONNECTION first.");

builder.Services.AddPersistentWorkflowsSqlServer(connectionString,
    configureExecution: options => options.MaximumAttempts = 5,
    configureWorker: options => options.MaximumConcurrency = 4);
builder.Services.AddWorkflow<OrderWorkflow, OrderData>();
builder.Services.AddScoped<ApproveOrder>();

using var host = builder.Build();
await host.Services.EnsurePersistentWorkflowsDatabaseAsync();

await using (var scope = host.Services.CreateAsyncScope())
{
    var runner = scope.ServiceProvider.GetRequiredService<IWorkflowRunner>();
    var accepted = await runner.EnqueueAsync<OrderWorkflow, OrderData>(
        "order-123", new OrderData { OrderId = 123 });
    await runner.SignalAsync(accepted.WorkflowInstanceId, "approval");
    Console.WriteLine($"Accepted workflow: {accepted.WorkflowInstanceId}");
}

await host.RunAsync();

public sealed class OrderData { public int OrderId { get; set; } }

public sealed class OrderWorkflow : IWorkflowDefinition<OrderData>
{
    public string Name => "order";
    public int Version => 1;
    public void Build(IWorkflowBuilder<OrderData> builder) =>
        builder.Step<ApproveOrder>("approve");
}

public sealed class ApproveOrder : IWorkflowStep<OrderData>
{
    public async Task<StepResult> ExecuteAsync(
        WorkflowExecutionContext<OrderData> context, CancellationToken ct)
    {
        var approval = await context.GetSignalAsync("approval", ct);
        return approval is null ? StepResult.WaitForSignal("approval") : StepResult.Success();
    }
}
```

The example records approval before the worker starts; signals can also arrive while a workflow is waiting. In your application, resolve `IWorkflowRunner` from a service scope or request scope to enqueue work, send signals, or inspect its state:

```csharp
var accepted = await runner.EnqueueAsync<OrderWorkflow, OrderData>(
    $"order-{orderId}", new() { OrderId = orderId }, cancellationToken);
await runner.SignalAsync(accepted.WorkflowInstanceId, "approval", cancellationToken: cancellationToken);
var state = await runner.GetAsync(accepted.WorkflowInstanceId, cancellationToken);
```

`EnqueueAsync` persists work and returns immediately. `StartAsync` also attempts inline execution after creating an instance. An existing business key does not execute again through `StartAsync`; workers pick up unfinished instances. Different serialized initial input with the same workflow name and key raises `WorkflowInputConflictException`.

## Database setup

The SQL Server package contains the initial and durable-execution migrations. Deploy migrations before starting workers. For a local sample:

```csharp
using PersistentWorkflows.EntityFrameworkCore.Persistence;
await host.Services.EnsurePersistentWorkflowsDatabaseAsync();
```

The runtime account needs normal data access; production schema changes should use a separately controlled deployment account. Moving migrations into the SQL Server assembly preserves their original migration IDs and history table. Existing instances upgrade with definition/context version 1; an old `Running` row without a lease becomes recoverable. Retain the original version 1 definition when upgrading.

## Guarantees and limits

- An accepted instance is durable. `Pending` instances and expired `Running` leases are discoverable by workers.
- Steps execute sequentially. Each successful step advances its cursor in the same transaction that records its outcome and context.
- Claims, renewals, and commits are fenced by an ownership token. Competing workers cannot independently commit progress under different tokens.
- An interrupted action can execute again, so actions and their external effects may occur multiple times. Successful external effects are not guaranteed: permanent failures, exhausted retries, or cancellation can stop execution. Make external effects idempotent; the engine does not provide exactly-once external effects.
- `context.IdempotencyKey` is stable across attempts. Pass it to external systems or use an application-side unique constraint/outbox and reconciliation.
- A lease cannot stop an external request already in flight. Timeouts/cancellation are cooperative; an action that ignores cancellation may continue after the engine stops waiting. Its late result is discarded.
- No distributed transaction, automatic compensation, parallel branches, or child-workflow orchestration is provided in this preview.
- Work progresses while a worker is running and its database is available. Terminal failures need explicit intervention. An unnamed indefinite wait (`StepResult.Wait()`) requires manual resume; a named signal wait (`StepResult.WaitForSignal(name)`) resumes when that signal is available or through manual resume.

See [USAGE.md](https://github.com/Sasanrf/PersistentWorkflows/blob/main/USAGE.md) for policies, operational APIs, versioning, and examples, and [docs/ARCHITECTURE.md](https://github.com/Sasanrf/PersistentWorkflows/blob/main/docs/ARCHITECTURE.md) for provider requirements.

## Examples and validation

Clone the [source repository](https://github.com/Sasanrf/PersistentWorkflows) and run these commands from its root directory. The sample projects and test suite are provided in the repository.

```sh
dotnet run --project PersistentWorkflows.Sample.Console
dotnet run --project PersistentWorkflows.Sample.Web --urls http://localhost:5080
dotnet test PersistentWorkflows.slnx
dotnet pack PersistentWorkflows.slnx --configuration Release --output artifacts/packages
```

Samples use dedicated sample databases on `localhost\SQLEXPRESS`. Override `PERSISTENTWORKFLOWS_CONNECTION` to select your database. The console starts a real hosted worker and demonstrates timer waiting; the web sample accepts orders and approval signals. Web endpoints are local demonstrations; integrate your application's authentication and authorization before deployment.

SQL Server conformance tests are opt-in through `PERSISTENTWORKFLOWS_TEST_SQLSERVER`. They create unique `PersistentWorkflowsTests_*` databases and delete only those databases. The supplied connection account must be permitted to create test databases. SQLite tests use temporary files and independent sessions.

## Package boundaries

| Package | Responsibility |
|---|---|
| PersistentWorkflows.Abstractions | Public definition, result, serialization, and persistence contracts |
| PersistentWorkflows.Core | Orchestration, policies, hosting, registry, and diagnostics |
| PersistentWorkflows.EntityFrameworkCore | Relational atomic persistence operations |
| PersistentWorkflows.EntityFrameworkCore.SqlServer | SQL Server dependencies, registration, and migrations |

Additional providers implement `IWorkflowStore` and run the provider conformance suite. Core has no EF dependency. See [docs/RELEASE.md](https://github.com/Sasanrf/PersistentWorkflows/blob/main/docs/RELEASE.md) for preview packaging and publication checks.
