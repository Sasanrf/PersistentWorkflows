# Validation — 0.1.0-preview.2

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
