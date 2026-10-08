# Workflow guarantee test audit — 2026-10-08

Follow-up: [targeted failure validation](FAILURE-TESTS.md) adds real worker-process termination, isolated database outage, and post-COMMIT acknowledgement-loss tests. The latest full run passes **116 tests**, including those five new cases. The measurements below describe the initial 111-case audit run.

The Release suite passed **111 tests, 0 failures, 0 skips** with SQL Server enabled: 20 Core tests and 91 persistence tests, including 43 SQL Server cases. This adds 38 executed cases to the previously documented 73: 21 SQL Server runner durability cases, five SQL Server integration/load cases, and six additional conformance cases on each of SQL Server and SQLite. No production code or existing assertions were changed.

A separate run with the opt-in variable absent passed 68 portable cases and explicitly skipped all 43 SQL Server cases, with no failures. Its TRX files are under `artifacts/test-audit/portable`. `git diff --check` passed.

## Audit scope and findings

Reviewed the guarantees in README.md, USAGE.md, and ARCHITECTURE.md against the test sources and the store/runner implementations. Previously, SQL Server exercised nine store contract cases and two migration cases. The 21 runner durability tests exercised SQLite only. There was no hosted multi-worker load measurement, deterministic worker concurrency/batch assertion, or database-injected checkpoint failure.

`SqlServerDurableWorkflowTests` now inherits the existing durability facts unchanged; only database configuration and lifecycle differ. It applies the packaged migrations in a unique SQL Server database for every test. The original SQLite facts still run.

| Documented guarantee | Existing evidence | Added evidence / remaining qualification |
|---|---|---|
| Durable acceptance and eventual discovery | Enqueue/provider restart; store due-query cases | All runner restart tests now also run on SQL Server; two real hosted workers drain a mixed backlog |
| Sequential actions; atomic history, context, cursor | Core ordering; store rollback on missing history | Two-step SQL workflows assert exact context trace, cursor and history; SQL trigger throws during history update and verifies full rollback, including version and ownership, then successful retry |
| Unique business keys; independent sessions | Concurrent create and claim conformance tests; input conflict test | Input conflict now runs on SQL Server; create/claim tests retained unchanged |
| Bounded discovery and worker concurrency | Store batch limit and future-wait exclusion | Gated worker test: 12 queued, batch five, concurrency three; exactly five complete, seven remain pending; five distinct scoped step instances, peak exactly three |
| Forced claims bypass waits but respect live/terminal ownership | Runner competing execution test | Conformance theory verifies future-wait bypass, no live-owner theft, rejected wrong-token begin, and no forced restart of succeeded/cancelled/failed instances |
| Lease renewal, stale-owner fencing, interrupted attempts | Expired owner cannot renew/commit/release; recovered attempt is abandoned | SQL runner heartbeat/loss tests; fresh provider recovers a seeded expired second-step attempt without replaying the successful first step |
| Atomic outcome and cancellation; repeat acknowledgement | Stale snapshot cancellation and repeated/conflicting commit tests | SQL runner cancellation during actions; injected SQL error rollback. Follow-up adds injected post-COMMIT acknowledgement loss with provider retry; actual transport loss remains untested |
| Shutdown release, cooperative cancellation, discarded late results | SQLite runner shutdown, cancellation, timeout and scope-disposal tests | Same exact tests now run on SQL Server; follow-up adds real separate-process termination and recovery |
| Retries, exhaustion, audited manual recovery | SQLite runner policies; store manual retry | SQL runner policy coverage; eight concurrent manual retries produce one winner and one matching audit reason, preserve cursor/context, reset failure budget |
| Stable external idempotency key; repeatable actions | SQLite simulated response-loss test | Same test on SQL Server; load asserts identical keys and exact attempt counts across retry/timer re-executions. This does not prove exactly-once external effects |
| Immutable early/late named signals | Store deduplication; SQLite early/late signal restart | SQL runner signal restart; 16 concurrent deliveries assert exactly one winner and its exact payload remains immutable |
| Detached deterministic bounded queries | Basic bounded store queries | Tied creation timestamps paginated across independent sessions with exact sequence/no duplicates; snapshot mutation cannot change storage |
| Bounded terminal retention, cascading cleanup, failure preservation, forgotten keys | Single completed-instance cleanup and failure preservation | Seven-instance mixed-status test deletes exactly two then four, preserves failed instance, verifies deleted history/signals and new identity on key reuse |
| Definition/context versions, migration, startup validation | SQLite versions/migration/definition rejection; SQL schema upgrade | Existing runner version/context migration and validation assertions now run on SQL Server |

The conformance additions live in `WorkflowStoreConformanceTests.Additional.cs` and are inherited by both relational providers. SQL integration and load cases live in `SqlServerWorkflowIntegrationTests.cs`.

## Measured run

Local Windows execution on 2026-10-08, .NET SDK 10.0.401, runtime .NET 10.0.12, SQL Server 17.0.1000.7 Express Edition (64-bit), 16 logical processors reported to the test process. SQL Server endpoint: `localhost\SQLEXPRESS`, integrated authentication. Tests used generated catalogs, not sample/application databases. The persistence test runner reported 13 seconds and Core reported 238 milliseconds; these exclude restore/build time.

| Suite | Passed |
|---|---:|
| Core | 20 |
| Existing SQLite runner durability | 21 |
| SQLite store conformance | 15 |
| SQL Server runner durability | 21 |
| SQL Server store conformance and migrations | 17 |
| SQL Server integration/load | 5 |
| Other persistence/repository tests | 12 |
| Total | 111 |

Each load case uses two independently built and started hosts in one test process, independent service providers/scopes and SQL sessions, batch size 16, maximum concurrency four per host, and 25 ms polling. Each workflow has two ordered steps. Equal quarters succeed immediately, retry once, wait on a timer once, or consume an early signal. The second step includes 20 ms simulated asynchronous work per attempt. Retry/timer delays are 100 ms. Acceptance is sequential before worker startup.

| Workflows | Persisted attempts | Acceptance incl. early signals (ms) | Drain (ms) | Workflows/sec | Peak active second-step actions, host 1 / host 2 |
|---|---:|---:|---:|---:|---:|
| 120 | 300 | 368.7 | 1,764.8 | 68.00 | 4 / 4 |
| 480 | 1,200 | 1,324.4 | 6,202.9 | 77.38 | 4 / 4 |

Drain timing includes host startup, polling until every instance succeeds, and graceful host shutdown. It excludes enqueueing, database creation/migrations, and final per-instance verification. Throughput is workflow count divided by drain seconds. These are individual local observations under the full test run, not a production capacity estimate or statistical benchmark. No performance assertion was loosened: the tests assert correctness and configured bounds, with a 90-second drain deadline solely to detect stalled execution.

For every load instance the test verifies succeeded status, cursor two, released lease, exact ordered context trace, exactly one first-step execution, exactly two successful outcomes, exact second-step attempt sequence, and the exact stable idempotency key on every attempt. Both hosts must execute work and stay within concurrency four. No due work may remain. The separate gated test measures the worker concurrency limit deterministically.

## Failures investigated

The first targeted SQL run had 33 passes and two failures in new tests. Both were test-author errors: expected idempotency keys used hyphenated GUIDs; `WorkflowExecutionContext.IdempotencyKey` explicitly uses `N` formatting. The load's key filter consequently found zero keys. Corrected the exact expected format and filter to `N`; retained exact identity, count and stability assertions. The subsequent 109-case run passed. After adding the larger load and injected SQL failure, the final 111-case run passed. No runtime defect was demonstrated by these runs.

The deliberate SQL failure is SQL error 51000 raised by a test-only trigger during the history update. The test requires that exact exception, verifies the preceding instance update rolled back, removes the trigger, and requires the same checkpoint to commit successfully. The trigger exists only inside that test's isolated database.

Build output includes XML-documentation and xUnit cancellation-token analyzer warnings; this audit does not claim a warning-free build. An initial sandboxed restore could not read the user NuGet configuration; the test commands were rerun with the required permission.

## Reproduction and isolation

From the repository root in PowerShell:

```powershell
$env:PERSISTENTWORKFLOWS_TEST_SQLSERVER = 'Server=localhost\SQLEXPRESS;Integrated Security=True;TrustServerCertificate=True'
dotnet test PersistentWorkflows.slnx -c Release --logger trx --results-directory artifacts/test-audit
# Narrow to the SQL integration/recovery/load cases:
dotnet test PersistentWorkflows.EntityFrameworkCore.Tests -c Release --filter 'FullyQualifiedName~SqlServerWorkflowIntegrationTests' --logger trx --results-directory artifacts/test-audit/integration
```

Supply a SQL Server account allowed to create/drop test databases. The supplied catalog is replaced with `PersistentWorkflowsTests_<random GUID>` per test. Teardown checks the exact generated catalog and namespace before deletion. The new shared fixture also queries `DB_ID` afterward and asserts absence; initialization failure attempts cleanup. No server-wide cleanup or application-database deletion is performed. A forcibly killed test process can still leave its generated database behind.

Raw local artifacts are ignored by Git: `artifacts/test-audit/sql-audit.trx` captures the first two failures, `full-run.log` captures the 109-case pass, and `final-run.log` plus `final/*.trx` capture the final run and load output. Use the default TRX filename as above so separate test projects do not overwrite one another. The checked-in measurements are preserved in this document.

## Remaining coverage gaps

- **Process/network failure:** the follow-up now covers actual worker termination, a generated database going offline/online, and execution-strategy identity/commit retries after injected post-COMMIT exceptions. SQL Server service restart, network partitions, actual connections severed after COMMIT, and deadlock injection remain untested.
- **Distributed timing:** the two hosts share one process and clock. No machine-to-machine clock skew, process pause longer than a lease, or multi-machine failover. Cancellation is covered at deterministic boundaries, not every commit/cancel interleaving.
- **Load scale:** 120/480 workflows are bounded mixed-backlog integration loads. No hours-long soak, sustained concurrent producers, large contexts/history, connection-pool exhaustion, index/query-plan analysis, resource measurements, percentile latency, or statistical repetitions. Both measured loads use the same batch/concurrency settings.
- **Policy/detail cases:** no exhaustive jitter distribution/backoff boundary matrix, exact timer wake-latency guarantee, custom serializer determinism/protection checks, diagnostic metric/span assertions, or SQL collation/case/accent matrix. Current pagination tests assume a quiescent dataset, not snapshot consistency while rows change.
- **External systems:** simulated effects/idempotency keys do not validate a real external API, application outbox, reconciliation, or atomic application-effect/engine-checkpoint enlistment. The documented absence of exactly-once external effects remains unchanged.
- **Platforms and deployment:** this run did not validate remote SQL Server, Linux SQL Server, other SQL editions, the GitHub-hosted CI execution, or a fresh packaged consumer. The prior release validation remains separate evidence for packaging.
