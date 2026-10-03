# Preview release procedure

## Validate

```sh
dotnet restore PersistentWorkflows.slnx
dotnet test PersistentWorkflows.slnx --configuration Release --no-restore
dotnet pack PersistentWorkflows.slnx --configuration Release --no-restore --output artifacts/packages
```

Run SQL Server tests with `PERSISTENTWORKFLOWS_TEST_SQLSERVER` pointing to an isolated test server. Fixtures replace its catalog with a unique test database and verify that name before deletion. Test current migrations and upgrade from InitialCreate. CI runs portable tests on Linux and SQL Server conformance tests on Windows LocalDB.

Inspect every package for assembly, XML documentation, dependencies, README, license metadata, and symbols. Install only `PersistentWorkflows.EntityFrameworkCore.SqlServer` into a clean .NET 10 consumer using the local feed, then execute a workflow. Verify dependency audit output. Run the console/web examples against dedicated sample databases.

The committed consumer has one package reference and no project references:

```sh
dotnet restore validation/PackageConsumer --configfile validation/PackageConsumer/NuGet.Config
dotnet run --project validation/PackageConsumer --no-restore
```

Set `PERSISTENTWORKFLOWS_TEST_SQLSERVER` first. The consumer creates a unique `PersistentWorkflowsPackageSmoke_*` database, executes two actions with a retry through the hosted worker, and removes that test database.

## Publish

The repository builds `0.1.0-preview.2` packages and symbol packages. Local packing does not publish anything. After reviewing release artifacts and confirming ownership of the package IDs, push the reviewed packages with the maintainer's NuGet credentials. Publish dependencies before the SQL Server installation package. Do not commit credentials or embed them in scripts.

Preview API changes must be described in release notes. This version adds provider separation, leases, recovery, scheduling, signals, and management APIs. Consumers of preview.1 should switch SQL Server registration to `AddPersistentWorkflowsSqlServer` so the moved migrations assembly is selected. Migrations retain their IDs; do not recreate an existing database.

## Stable-release gate

- Recovery, concurrent ownership, cancellation, signals, transaction rollback, migrations, and clean consumer installation pass on supported databases/platforms.
- Public API compatibility and names are reviewed; execution and external-effect guarantees remain explicit.
- Long-running/load and deployment-upgrade behavior are evaluated in real integrations, beyond the automated conformance tests.
- Every claimed provider has its own migration and conformance coverage.
- Package ownership, release notes, and support policy are confirmed by the maintainer.

The preview deliberately does not claim exactly-once external effects or support for every database/application runtime.
