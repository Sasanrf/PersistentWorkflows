# Stable release validation — 1.0.0

Release preparation on 2026-10-08. Local checks below passed; hosted release-commit CI and authenticated NuGet publication are tracked separately. A built package is not evidence of publication.

## Completed local checks

- Public API review: API-REVIEW-1.0.md; compatibility/support policy: SUPPORT.md; release notes: CHANGELOG.md.
- Version 1.0.0 regression: **116 passed, zero failed/skipped** (20 Core, 96 persistence), with SQL Server enabled. Test databases are isolated and cleaned up.
- Forced restore passed with transitive auditing and NU1900–NU1905 warnings treated as errors. The direct/transitive vulnerability report listed no vulnerable packages from configured advisory sources. This is not a guarantee that dependencies have no undisclosed vulnerabilities.
- All four 1.0.0 packages packed successfully with SDK API/package compatibility validation against 0.1.0-preview.2; no compatibility suppressions.
- Package inspection passed for assemblies, XML documentation, README, MIT license metadata, PDB symbols, identities and internal dependency versions. Hashes are in `artifacts/stable-release/package-manifest.json`.
- Repository NuGet-only smoke consumer passed on 1.0.0: both packaged migrations, two actions, one retry, three attempts, succeeded; generated smoke database removed.
- Independent consumer passed on the exact local 1.0.0 packages: **241 workflows, 627 verified attempts**, two producer and two worker processes, including real crash/recovery. 193 workflows succeeded and 48 were intentionally cancelled. Build had zero warnings/errors. Generated database deletion was confirmed.

The independent final-package run is `D:\Project\PersistentWorkflowsTest\runs\70d161a755a14c409df0d20730131a2e`: 16.218 seconds workload/drain, paced throughput 14.80 workflows/sec, terminal p50 214.79 ms / p95 886.36 ms. Its `packages.json` records the exact package hashes, `results.json` records assertions/measurements, and `cleanup.json` confirms deletion. No 30-minute rerun is claimed for this final version.

Existing XML-documentation/cancellation-token analyzer warnings remain; this is not a warning-free library build. The sample web project is deliberately not packable and emits an informational packaging warning.

Raw local evidence: `artifacts/stable-release/restore.log`, `tests.log`, `tests/*.trx`, `dependency-audit.json`, `pack.log`, and `package-manifest.json`. Artifacts are ignored by Git. Package output: `artifacts/packages/*1.0.0.nupkg` and matching `.snupkg` files.

## Independent consumer soak evidence

The separate app at `D:\Project\PersistentWorkflowsTest` ran against local package `0.1.0-rc.validation.20261008.1`, the same production implementation/schema promoted to 1.0.0. It uses NuGet references only. This is candidate soak evidence, not a new soak of the final versioned packages.

Run: `runs/11fe2eba0cdb468f9f480e61dac0734b`, completed 2026-10-08 at 21:28 Tallinn time. SQL Server 17.0.1000.7 Express, .NET runtime 10.0.12.

| Measurement | Result |
|---|---:|
| Producer duration | 1,800 seconds |
| Separate producers / workers | 2 / 2 |
| Workflows | 31,041 |
| Succeeded / intentionally cancelled | 24,833 / 6,208 |
| Verified attempts | 80,707 |
| Workload plus drain | 1,809.95 seconds |
| Paced throughput | 17.15 workflows/sec |
| Terminal latency p50 / p95 | 190.19 / 300.38 ms |
| Peak private memory, worker 1 / 2 | 47.34 / 48.61 MiB |
| Peak working set, worker 1 / 2 | 125.01 / 126.11 MiB |

Both workers' private memory settled around 43–44 MiB after warm-up. Five-minute averages showed no sustained upward trend over the run. All child stderr files were empty, no failure file was present, and cleanup confirmed deletion of the generated database. Samples cover worker processes, not SQL Server memory or connection-pool internals. Throughput includes deliberate producer pacing and is not a capacity limit.

## Final gates

- Exact 1.0.0 package consumers: completed as recorded above.
- Hosted CI: [PR #1](https://github.com/Sasanrf/PersistentWorkflows/pull/1) passed portable checks but initially failed Windows SQL Server checks on commit `f09f1f3`. The logs showed the crash helper building in Debug while tests expected Release, and fixed-delay shutdown cancellation racing with database setup. The follow-up includes FailureWorker in the solution, fails the build if its expected output is absent, and waits for action entry before shutdown cancellation. The `Pending` and cancellation assertions remain unchanged. All 116 local tests and the three affected cases in a fresh source copy passed. Hosted CI must rerun on the fix before merge/publication; it is not yet verified green.
- Publish only with authenticated maintainer NuGet access after those gates pass. No NuGet API key was available in the release session's environment or configured NuGet API-key entries when checked.
- After publication, repeat a NuGet.org smoke install. Transport-level acknowledgement loss, server failover, other deployment combinations and external business-system idempotency retain the limitations documented in the test reports.
