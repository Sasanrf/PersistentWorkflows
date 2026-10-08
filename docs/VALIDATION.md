# Validation — 0.1.0-preview.2

Latest: [1.0.0 release preparation and validation](STABLE-VALIDATION.md), including final-package checks and pending hosted CI/publication. The records below retain the earlier preview evidence.

An expanded [workflow guarantee audit](TEST-AUDIT.md) on 2026-10-08 passed 111 tests with SQL Server enabled, including runner recovery, injected checkpoint failure, concurrency bounds, and measured 120/480-workflow loads. That report maps guarantees to tests and records remaining coverage gaps. The release checks below describe the earlier 2026-10-03 validation.

The subsequent [targeted failure validation](FAILURE-TESTS.md) passed 116 tests, adding real worker termination, isolated database outage/recovery, and post-COMMIT acknowledgement-loss retry scenarios.

Validated locally on 2026-10-03 with .NET SDK 10.0.401, Release configuration.

| Check | Result |
|---|---|
| Core tests | 20 passed |
| Persistence, durability, SQLite, and SQL Server tests | 53 passed |
| Total | 73 passed, 0 failed, 0 skipped |
| SQL Server migrations | Initial creation, current-model consistency, and legacy-instance upgrade passed |
| Clean package consumer | One SQL Server package reference; migrations, hosted worker, two actions, and retry passed |
| Dependency audit | No known vulnerable packages reported by configured advisory sources |
| Git diff whitespace check | Passed |

Tests cover competing creates/claims, expired leases, renewal during long actions, stale-owner fencing, checkpoint rollback, repeated commit acknowledgement, restart recovery, cancellation, retries/exhaustion, explicit recovery audit, early/late signals, retained definition versions, context migration across waiting/resume, async scope disposal, discarded late action results, bounded queries, and retention.

SQL Server tests and the package consumer used uniquely named temporary databases and removed them afterward. SQLite conformance tests used temporary file databases and independent sessions.

Local artifacts are in `artifacts/packages`: four `.nupkg` files and four `.snupkg` files. Packages include assemblies, XML documentation, README, dependency metadata, and MIT license metadata. Detailed local logs are in `artifacts/release-tests.log`, `artifacts/package-consumer.log`, and `artifacts/dependency-audit.log`.

At the time of this local validation session, the GitHub CI workflow had been added but had not been executed on GitHub, and packages had not yet been published to NuGet. Subsequently, the maintainer published and listed all four `0.1.0-preview.2` packages on NuGet.org. Publication does not add validation coverage. This is a preview; integration/load validation and maintainer approval of public APIs/package ownership remain part of the stable-release procedure in [RELEASE.md](RELEASE.md).
