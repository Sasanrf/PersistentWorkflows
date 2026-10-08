# Stable release procedure

The repository targets 1.0.0. Local packing does not publish packages. See [stable validation](STABLE-VALIDATION.md), [API review](API-REVIEW-1.0.md), [support policy](SUPPORT.md), and [release notes](../CHANGELOG.md).

## Validate

```sh
dotnet restore PersistentWorkflows.slnx
dotnet test PersistentWorkflows.slnx --configuration Release --no-restore
dotnet pack PersistentWorkflows.slnx --configuration Release --no-restore --output artifacts/packages
dotnet list PersistentWorkflows.slnx package --vulnerable --include-transitive --format json
```

Run SQL Server tests with `PERSISTENTWORKFLOWS_TEST_SQLSERVER` pointing to an isolated test server. Fixtures replace its catalog with a unique test database and verify that name before deletion. Test current migrations and upgrade from InitialCreate. CI runs portable tests on Linux and SQL Server conformance tests on Windows LocalDB.

Inspect every package for assembly, XML documentation, dependencies, README, license metadata, and symbols. Install only `PersistentWorkflows.EntityFrameworkCore.SqlServer` into a clean .NET 10 consumer using the local feed, then execute a workflow. Verify dependency audit output. Run the console/web examples against dedicated sample databases.

Run `./validation/Inspect-Packages.ps1` from PowerShell to enforce package contents, versions and internal dependencies and record package/symbol SHA-256 hashes. Restore audits transitive dependencies and fails on vulnerability/advisory-access warnings. All four packages run SDK package compatibility validation against published preview.2 during packing; do not suppress compatibility errors to pass the release.

The GitHub Windows SQL Server and Linux portable jobs must pass on the release commit. A local Windows run or CI on an earlier commit does not establish this. CI retains test/package artifacts and can be dispatched manually.

The committed consumer has one package reference and no project references:

```sh
dotnet restore validation/PackageConsumer --configfile validation/PackageConsumer/NuGet.Config
dotnet run --project validation/PackageConsumer --no-restore
```

Set `PERSISTENTWORKFLOWS_TEST_SQLSERVER` first. The consumer creates a unique `PersistentWorkflowsPackageSmoke_*` database, executes two actions with a retry through the hosted worker, and removes that test database.

## Publish

The repository builds `1.0.0` packages and symbol packages. After the release-commit CI and artifact checks pass, push those exact reviewed packages with the maintainer's NuGet credentials. Publish Abstractions, Core, EntityFrameworkCore, then the SQL Server installation package. Include matching symbols, retain hashes, and do not rebuild between reviewing and publishing. Do not commit credentials or embed them in scripts. If publishing authentication is unavailable, publication remains pending.

There are no production C# or migration changes from preview.2 to 1.0.0. Consumers of preview.1 should switch SQL Server registration to `AddPersistentWorkflowsSqlServer` so the moved migrations assembly is selected. Migrations retain their IDs; do not recreate an existing database. Once NuGet indexes the release, repeat a smoke installation from NuGet.org without local source mapping and record the publication URL/tag.

## Stable-release gate

- Recovery, concurrent ownership, cancellation, signals, transaction rollback, migrations, and clean consumer installation pass on supported databases/platforms.
- Public API compatibility and names are reviewed; execution and external-effect guarantees remain explicit.
- Long-running/load and deployment-upgrade behavior are evaluated in real integrations, beyond the automated conformance tests.
- Every claimed provider has its own migration and conformance coverage.
- Package ownership, release notes, and support policy are confirmed by the maintainer.

Version 1.0 does not claim exactly-once external effects or support for every database/application runtime.
