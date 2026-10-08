# Support and compatibility for 1.x

## Scope

- Target framework: .NET 10. Packages use EF Core 10 and Microsoft.Data.SqlClient through the SQL Server provider.
- Shipped database provider: SQL Server. Local integration/failure/load validation used SQL Server 17.0.1000.7 Express on Windows. CI is configured for Windows LocalDB plus portable tests on Linux; configuration alone is not evidence that a specific commit passed CI.
- SQLite is a relational contract test target, not a shipped migration/provider package. Other providers, operating-system/database combinations, Native AOT, and trimmed deployments are not certified by this validation.
- Sequential workflows only. Guarantees and exclusions are specified in README.md, USAGE.md, and ARCHITECTURE.md. Stable does not mean exactly-once external effects or unlimited production scale.

## Public API commitment

Use semantic versioning: 1.x patch/minor releases preserve the supported public API and documented behavior. Intentional breaking API or persistence changes require a major release and migration instructions. Security/correctness fixes may reject previously invalid usage; document those changes in release notes.

The primary application surface is the workflow definition/builder/step interfaces, execution context/results, runner APIs, registration/options, and serializer/context migrator extension points. `IWorkflowStore` and state types form the provider extension contract. Existing public repository/unit-of-work APIs, EF DbContext/entities, and registry/definition types remain public and are included in compatibility checking; their low-level presence does not authorize bypassing ownership rules. Prefer runner/store operations for live instances.

Stored workflow definitions are separately versioned by applications. Package version changes do not change a workflow's definition version or make old definitions disposable. Do not remove definitions needed by unfinished instances. Signals remain immutable one-shot receipts; retention removes business-key deduplication records.

## Operational support

Use the repository issue tracker for reproducible bugs, including package/runtime/SQL versions and sanitized logs. Support is best-effort community/maintainer support; no response-time, availability, or commercial support SLA is promised. Never include secrets or private workflow context in public reports. Keep dependency updates and operational monitoring in your application's deployment process.
