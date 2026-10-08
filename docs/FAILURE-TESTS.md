# Targeted failure validation — 2026-10-08

Added five SQL Server cases following the [initial guarantee audit](TEST-AUDIT.md). The targeted run passed all five; the full Release regression run passed **116 tests, 0 failures, 0 skips** (20 Core, 96 persistence; 48 SQL Server cases). No production code or previous assertions were changed. No runtime defect was demonstrated by these runs.

## Scenarios and required outcomes

| Case | Fault and assertions |
|---|---|
| Worker process killed during second action | Launch the real hosted worker in a separate `dotnet` process. Wait for its second action to report entry and independently verify SQL contains first-step success and a started second attempt. Terminate that exact process without graceful shutdown. Require the persisted instance to retain its running state and ownership token. Start a different process and require automatic recovery after the lease becomes available. The first step has one successful execution; the second has exactly abandoned attempt 1 and successful attempt 2; context is exactly `[first, second]`, cursor is two, and ownership is released. Both processes report the same second-step idempotency key. |
| Database unavailable during active action | Take only the generated test database offline with rollback immediate. Require a fresh SQL connection to fail and the active action to receive cancellation from failed lease renewal, without cancelling the caller or expiring the step timeout. Restore the database. The same running host must recover without manual resume/retry. Require exact history as above, unchanged idempotency key, and no leaked in-memory context mutation from the interrupted action. |
| Lost acknowledgement after attempt creation | An EF transaction interceptor independently reads the committed attempt, then throws `TimeoutException` after the real SQL COMMIT. SQL Server's configured execution strategy retries inside `BeginStepAsync`. Require the returned attempt to have the original committed GUID, attempt number one, and started status; history must contain exactly one attempt. Two transaction-commit callbacks prove the retry path executed. |
| Lost acknowledgement after a nonterminal outcome | Inject the same post-COMMIT timeout after recording a successful step while the workflow remains running. Require the retry to acknowledge success, with version incremented exactly once, cursor one, exact context, retained ownership, and one matching history row. A conflicting repeated outcome must still be rejected. |
| Lost acknowledgement after a terminal outcome | Repeat the outcome case with succeeded status. Require one version increment and one history row despite the original commit having released ownership; the repeated operation must acknowledge the durable outcome without claiming/replaying it. |

The acknowledgement tests explicitly configure `EnableRetryOnFailure(2, TimeSpan.Zero, null)` on the test store. The packaged SQL Server registration does **not** enable this option by default. These cases validate the provider retry contract when enabled; they do not claim the default runner transparently retries every connection failure.

The lost acknowledgement is an injected exception at EF's post-commit boundary, **not an actual severed network connection**. Independent SQL reads inside the interceptor prove the transaction was durable before the exception. Fault counters and version/history assertions prevent the tests from passing without exercising the intended fault and retry.

## Measurements

Same local environment as the initial audit: SQL Server 17.0.1000.7 Express, .NET SDK 10.0.401 / runtime 10.0.12, Windows, 16 logical processors. Three-second leases and 200 ms renewal intervals are test settings.

| Measurement | Targeted run | Full regression run |
|---|---:|---:|
| Process-kill recovery, including replacement process startup | 3,281.7 ms | 3,218.5 ms |
| Database recovery after ONLINE completes | 109.5 ms | 2,980.8 ms |
| Lost attempt-creation acknowledgement call, including injected failure/retry | 43.7 ms | 57.8 ms |
| Lost running-outcome acknowledgement call | 19.9 ms | 29.4 ms |
| Lost terminal-outcome acknowledgement call | 92.6 ms | 93.8 ms |

Database recovery can use successful release or lease expiry depending on when the in-flight cleanup observes connectivity returning; the assertion requires recovery in either case. These are observations, not latency guarantees. The five-case targeted suite reported 14 seconds. The full persistence suite reported 17 seconds; Core reported 399 ms, excluding build/restore. Build retains XML-documentation and cancellation-token analyzer warnings.

## Run and isolation

```powershell
$env:PERSISTENTWORKFLOWS_TEST_SQLSERVER = 'Server=localhost\SQLEXPRESS;Integrated Security=True;TrustServerCertificate=True'
dotnet test PersistentWorkflows.EntityFrameworkCore.Tests -c Release --filter 'FullyQualifiedName~SqlServerFailureTests' --logger trx --results-directory artifacts/test-audit/failures
dotnet test PersistentWorkflows.slnx -c Release --logger trx --results-directory artifacts/test-audit/failure-regression
```

The account needs create/drop database permissions and permission to take its generated test database offline/online. The SQL Server service and other databases are never stopped or altered. The fixture validates its exact generated database name before changing availability, restores ONLINE in a `finally` block and again during fixture cleanup if necessary, then drops that database and verifies its absence.

`validation/FailureWorker` is a non-packable console project built through the test project's project reference and copied into the test output. The tests start it without a window, pass the connection through a process environment variable rather than command-line arguments, and terminate only child processes they created. The worker itself refuses catalogs that do not match the generated test naming format. Per-test deadlines bound waiting, and child processes are reaped before database teardown. If the entire test runner is forcibly terminated, its normal cleanup cannot be guaranteed.

Local raw evidence: `artifacts/test-audit/failure-run.log`, `failures/*.trx`, `failure-regression.log`, and `failure-regression/*.trx`. These files are ignored by Git; this report preserves measured results.

## Remaining limits

Still untested: actual transport loss after COMMIT, SQL Server service restart/failover, network partitions, injected deadlocks, cross-machine clock skew, and a sustained multi-process producer/worker soak. The database outage case disables pooling and connection retries to make failure detection bounded, so it does not validate every production connection-pool/reconnect configuration. Killing a worker before its checkpoint demonstrates replay and recovery; it does not establish exactly-once external effects. The initial audit's other scale, diagnostics, serialization, platform and external-system gaps remain.
