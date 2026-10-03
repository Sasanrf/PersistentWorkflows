using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PersistentWorkflows.Abstractions.Persistence;
using PersistentWorkflows.EntityFrameworkCore.Persistence;
using Xunit;

namespace PersistentWorkflows.EntityFrameworkCore.Tests.Execution;

/// <summary>Opt in with PERSISTENTWORKFLOWS_TEST_SQLSERVER. Creates and deletes only a uniquely named test database.</summary>
public sealed class SqlServerWorkflowStoreTests : WorkflowStoreConformanceTests, IAsyncLifetime
{
    private readonly string _databaseName = $"PersistentWorkflowsTests_{Guid.NewGuid():N}";
    private string? _connectionString;
    private DbContextOptions<PersistentWorkflowsDbContext> Options => new DbContextOptionsBuilder<PersistentWorkflowsDbContext>()
        .UseSqlServer(_connectionString!, sql => sql.MigrationsAssembly(typeof(SqlServer.ServiceCollectionExtensions).Assembly.GetName().Name)).Options;
    protected override IWorkflowStore CreateStore() => new EfWorkflowStore(Options);
    public async ValueTask InitializeAsync()
    {
        var connection = Environment.GetEnvironmentVariable("PERSISTENTWORKFLOWS_TEST_SQLSERVER");
        if (string.IsNullOrWhiteSpace(connection)) Assert.Skip("Set PERSISTENTWORKFLOWS_TEST_SQLSERVER to run isolated SQL Server conformance tests.");
        _connectionString = new SqlConnectionStringBuilder(connection) { InitialCatalog = _databaseName }.ConnectionString;
        await using var db = new PersistentWorkflowsDbContext(Options);
        await db.Database.MigrateAsync();
    }
    public async ValueTask DisposeAsync()
    {
        if (_connectionString is null) return;
        var catalog = new SqlConnectionStringBuilder(_connectionString).InitialCatalog;
        if (catalog != _databaseName || !catalog.StartsWith("PersistentWorkflowsTests_", StringComparison.Ordinal))
            throw new InvalidOperationException("Refusing cleanup of a database outside the test namespace.");
        await using var db = new PersistentWorkflowsDbContext(Options);
        await db.Database.EnsureDeletedAsync();
    }
    [Fact]
    public async Task MigrationsMatchTheRuntimeModelAndUpgradeLegacyInstances()
    {
        await using var db = new PersistentWorkflowsDbContext(Options);
        Assert.False(db.Database.HasPendingModelChanges());
        var applied = await db.Database.GetAppliedMigrationsAsync();
        Assert.Equal(2, applied.Count());
    }
    [Fact]
    public async Task LegacyRunningInstancesUpgradeWithValidVersionsAndBecomeRecoverable()
    {
        await using var db = new PersistentWorkflowsDbContext(Options);
        var migrator = Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>(db);
        await migrator.MigrateAsync("20260601213338_InitialCreate");
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO [PersistentWorkflows].[WorkflowInstances] ([Id],[WorkflowName],[InstanceKey],[ContextJson],[Status],[CurrentStepIndex],[CreatedAtUtc],[UpdatedAtUtc],[Version]) VALUES ({id},{"legacy"},{"key"},{"{}"},{"Running"},{0},{Now},{Now},{1})");
        await migrator.MigrateAsync();
        var state = (await CreateStore().GetAsync(id, default))!;
        Assert.Equal(1, state.DefinitionVersion);
        Assert.Equal(1, state.ContextSchemaVersion);
        Assert.Contains(id, await CreateStore().GetDueAsync(Now, 10, default));
    }
}
