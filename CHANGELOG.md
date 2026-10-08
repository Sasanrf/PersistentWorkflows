# Changelog

## 1.0.0 — prepared for release

First stable release of sequential durable workflows for .NET 10 with SQL Server persistence. Publication is tracked separately from local package creation.

### Included

- Durable acceptance, sequential checkpoints, fenced leases, background recovery, retries, timers, one-shot signals, cooperative cancellation, and audited manual recovery.
- Definition/context versioning, bounded operational queries, and terminal retention.
- A single SQL Server installation package with transitive Core, Abstractions, and EF persistence packages.

### Changes from 0.1.0-preview.2

- No application-facing API, workflow implementation, or database migration changes. Existing migration IDs are retained. Updating the package version does not require recreating databases or rewriting workflow definitions.
- Expanded SQL Server integration, crash/outage recovery, concurrency, and post-COMMIT retry validation. A separate NuGet-only consumer completed a 30-minute multi-process soak with 31,041 workflows and 80,707 attempts.
- Build-time package compatibility checks against published preview.2 and dependency-audit failures enforced during restore.
- Explicit 1.x support/compatibility scope and reproducible release evidence.

### Operational notes

Actions may execute again after interruption; external effects must be idempotent. There is no exactly-once external-effect guarantee, distributed transaction, compensation, branching, or child-workflow orchestration. Cancellation and timeouts are cooperative. SQL Server registration does not enable EF transient retries by default. Keep original definitions for unfinished instances and deploy migrations before workers.

Consumers of preview.1 should use `AddPersistentWorkflowsSqlServer` so migrations resolve from the SQL Server assembly. Preserve migration history and existing instance data.
