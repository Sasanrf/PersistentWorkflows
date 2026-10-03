# Usage

## Choose execution mode

`EnqueueAsync<TWorkflow,TContext>` durably accepts work without executing application actions on the caller. Use it in web requests and for background jobs. `StartAsync` creates work and attempts execution inline, returning its current status; it may return `Waiting` or `Failed`. Neither method promises that acceptance means completion.

`ResumeAsync(id)` is a manual override that can resume a timer wait early or an indefinite wait. It never steals an unexpired lease and does nothing for terminal instances. Background processing respects due dates and signals. Use the worker in every deployment responsible for eventual completion.

Configure an API-only process with `AddPersistentWorkflowsSqlServer(connection, enableWorker: false)`. A separate worker process registers the same definitions with `enableWorker: true` and starts its host:

```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddPersistentWorkflowsSqlServer(connectionString);
builder.Services.AddWorkflow<OrderWorkflow, OrderData>();
builder.Services.AddScoped<ApproveOrder>();
using var host = builder.Build();
await host.RunAsync();
```

The console sample demonstrates an embedded worker. The web sample demonstrates HTTP acceptance, status, approval, and cancellation. Always resolve runners from scopes.

## Action outcomes

| Outcome | Meaning |
|---|---|
| `StepResult.Success()` | Persist updated context and advance to the next action |
| `StepResult.Retry(message, retryAfter)` | Retry this action, with a default schedule when the delay is omitted |
| `StepResult.Fail(code, message)` | Stop with a terminal failure; earlier business effects remain |
| `StepResult.Wait(delay)` | Recheck this action after the delay |
| `StepResult.Wait()` | Wait indefinitely until explicit manual resume |
| `StepResult.WaitForSignal(name)` | Wake when a durable named signal is available |

Throwing from an action is classified by `IsRetryableException`. By default exceptions are retryable. Invalid outcomes fail explicitly. Context changes are persisted for returned outcomes; a thrown exception preserves the previous durable context because partial in-memory changes are not a completed outcome.

Workflow and step names and business keys are limited to 200 characters. Step names beginning with `$` are reserved for audit events. Error codes/messages are bounded to 100/4000 characters.

## Retry and execution policy

```csharp
services.AddPersistentWorkflowsSqlServer(connectionString, configureExecution: o =>
{
    o.MaximumAttempts = 5;
    o.InitialRetryDelay = TimeSpan.FromSeconds(5);
    o.MaximumRetryDelay = TimeSpan.FromMinutes(5);
    o.RetryJitterRatio = 0.2;
    o.StepTimeout = TimeSpan.FromMinutes(5);
    o.LeaseDuration = TimeSpan.FromMinutes(1);
    o.LeaseRenewalInterval = TimeSpan.FromSeconds(15);
    o.IsRetryableException = ex => ex is IOException or TimeoutException;
}, configureWorker: o =>
{
    o.PollingInterval = TimeSpan.FromSeconds(10);
    o.BatchSize = 100;
    o.MaximumConcurrency = 4;
});
```

Retries use exponential backoff and jitter. An explicit retry delay overrides backoff. `MaximumAttempts` limits consecutive retryable outcomes for the current action; successful actions, ordinary waiting outcomes, and explicit manual recovery reset the failure counter. Historical attempt numbers remain monotonic, including recovered interrupted attempts. Lease duration, action timeout, maximum automatic retry delay, and polling intervals are bounded to 30 days; use durable waiting for longer business delays.

Workers fetch bounded batches and use a fresh execution scope per instance. Ownership is renewed during long actions. Keep clocks synchronized between processes and choose leases with margin for database latency and pauses. Register a custom `TimeProvider` before the engine to control time in tests.

## Signals

```csharp
await runner.SignalAsync(id, "approval", "{\"approved\":true}", cancellationToken);
```

Each signal name identifies one immutable receipt per workflow instance. The first payload wins; duplicate delivery returns `false`. A null payload is still a received signal. Signals may arrive before the workflow waits. In an action, use `await context.GetSignalAsync(name, ct)` to distinguish absence from a null payload and read early signals. Receipts are retained until instance cleanup, so different steps may observe the same signal. Use distinct names for distinct events; this is a one-shot inbox, not a streaming event queue. Signals require an existing workflow ID.

## Query, cancel, recover, and clean up

```csharp
var state = await runner.GetAsync(id, ct);
var failures = await runner.ListAsync(WorkflowStatus.Failed, skip: 0, take: 100, cancellationToken: ct);
var history = await runner.GetHistoryAsync(id, skip: 0, take: 100, cancellationToken: ct);
await runner.CancelAsync(id, ct);
await runner.RetryAsync(id, "Dependency repaired; approved by operator", ct);
await runner.DeleteCompletedAsync(DateTime.UtcNow.AddDays(-90), limit: 100, cancellationToken: ct);
```

Queries return detached snapshots; changing them does not alter execution. `ExecutionStatus` exposes the typed status; `Status` retains its persisted string representation. Pages support 1–1000 records. Applications control authorization, tenancy, and which context/error fields they expose.

Cancellation prevents pending/waiting actions from starting and requests cooperative interruption of running actions. External effects already made are retained. Terminal instances return `false` for cancellation. Manual retry accepts only `Failed`, requires a reason, preserves the cursor/context, resets its retry budget, and records a `ManualRetry` audit event. Cancelled/succeeded instances are not restarted.

Cleanup deletes old succeeded/cancelled instances with their history and signals. Failed instances are retained for investigation. Deleting an instance also forgets its business-key deduplication record: do not reuse old keys when permanent deduplication matters. Maintain business-level deduplication independently if required.

## Definition and context versions

`IWorkflowDefinition.Version` and `ContextSchemaVersion` default to 1. Increment the definition version for changed business behavior, action order, or identity. Register old and new definitions as separate CLR types with the same name and different versions. New starts select their requested type; old instances select their persisted version.

The stored fingerprint detects changes in action names/types/order within the same version. It cannot detect changes inside an action's method body; version those changes yourself. A missing version or changed fingerprint records `DefinitionUnavailable` and leaves the cursor unchanged. Restore the correct definition and explicitly retry.

Register `IWorkflowContextMigrator` when raising a context schema version within a definition. It receives the workflow name, definition version, source/target schema versions, and persisted JSON. Return migrated JSON without external effects. The default migrator rejects version mismatches. Migration is committed with the next action outcome; design it to be repeatable after interruption. Do not downgrade context schemas.

Business keys are scoped to workflow name, not definition version. A new version does not automatically create a second instance for an existing business key. Input identity uses the SHA-256 hash of the serialized initial context; custom serializers must emit deterministic JSON for semantically identical input.

## External effects and transactions

Pass `context.IdempotencyKey` to an external API, or enforce it in your application database. Its format is `<workflow-id>:<step-name>` and it does not change with attempts. Register distinct actions for distinct effects when each needs independent progress tracking.

Engine outcome/history/cursor commits are atomic. Application DbContexts are separate and are not enlisted in that engine transaction. Use an application transaction/outbox, deduplication, or reconciliation to bridge an external effect and the engine checkpoint. `IWorkflowUnitOfWork` is a legacy EF save convenience, not a distributed transaction or an action/checkpoint transaction coordinator.

## Diagnostics and serialization

The `PersistentWorkflows` ActivitySource and Meter expose workflow/step spans, accepted work, retries, ownership losses, outcome counts, and action duration. Subscribe using your tracing/metrics stack; no telemetry exporter is required by this library. IDs belong to spans/log scopes, not metric labels.

Structured logs include workflow identity, action, attempt, and outcomes. Context JSON and signal payloads are not logged by the engine. Exception messages may contain application data; apply your application's logging policy.

The default serializer uses System.Text.Json with camel-case property names. Register `IWorkflowContextSerializer` to change serialization or protect persisted context. The application remains responsible for keys, retention, and signal-payload protection. Persisted context should contain durable values and identifiers, not service instances or request objects.
