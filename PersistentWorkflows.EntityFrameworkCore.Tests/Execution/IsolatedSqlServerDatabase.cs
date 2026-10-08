using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PersistentWorkflows.EntityFrameworkCore.Persistence;
using Xunit;

namespace PersistentWorkflows.EntityFrameworkCore.Tests.Execution;

/// <summary>One migrated database per test, including inherited facts. Never uses the supplied catalog.</summary>
internal sealed class IsolatedSqlServerDatabase : IAsyncDisposable
{
    private readonly string _name = $"PersistentWorkflowsTests_{Guid.NewGuid():N}";
    private bool _offline;
    public string ConnectionString { get; private set; } = "";
    public void Configure(DbContextOptionsBuilder options) => options.UseSqlServer(ConnectionString,
        sql => sql.MigrationsAssembly(typeof(SqlServer.ServiceCollectionExtensions).Assembly.GetName().Name));
    public PersistentWorkflowsDbContext Open()
    {
        var options = new DbContextOptionsBuilder<PersistentWorkflowsDbContext>();
        Configure(options);
        return new(options.Options);
    }
    public async ValueTask InitializeAsync()
    {
        var connection = Environment.GetEnvironmentVariable("PERSISTENTWORKFLOWS_TEST_SQLSERVER");
        if (string.IsNullOrWhiteSpace(connection)) Assert.Skip("Set PERSISTENTWORKFLOWS_TEST_SQLSERVER to run isolated SQL Server tests.");
        ConnectionString = new SqlConnectionStringBuilder(connection) { InitialCatalog = _name }.ConnectionString;
        try
        {
            await using var db = Open();
            await db.Database.MigrateAsync();
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }
    public async ValueTask DisposeAsync()
    {
        if (ConnectionString.Length == 0) return;
        if (_offline) await SetOnlineAsync(true);
        var catalog = new SqlConnectionStringBuilder(ConnectionString).InitialCatalog;
        if (catalog != _name || !catalog.StartsWith("PersistentWorkflowsTests_", StringComparison.Ordinal))
            throw new InvalidOperationException("Refusing cleanup outside this test's database.");
        await using var db = Open();
        await db.Database.EnsureDeletedAsync();
        await using var master = new SqlConnection(new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = "master" }.ConnectionString);
        await master.OpenAsync();
        await using var command = master.CreateCommand();
        command.CommandText = "SELECT DB_ID(@name)";
        command.Parameters.AddWithValue("@name", _name);
        Assert.Equal(DBNull.Value, await command.ExecuteScalarAsync());
    }

    // Change only this generated catalog, never the SQL Server service or another database.
    public async Task SetOnlineAsync(bool online)
    {
        if (new SqlConnectionStringBuilder(ConnectionString).InitialCatalog != _name
            || !Guid.TryParseExact(_name["PersistentWorkflowsTests_".Length..], "N", out _))
            throw new InvalidOperationException("Refusing to alter a database outside this fixture.");
        await using var master = new SqlConnection(new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = "master", Pooling = false }.ConnectionString);
        await master.OpenAsync();
        await using var command = master.CreateCommand();
        command.CommandText = $"ALTER DATABASE [{_name}] SET {(online ? "ONLINE" : "OFFLINE WITH ROLLBACK IMMEDIATE")}";
        if (!online) _offline = true; // Cleanup must restore even if the command's acknowledgement is lost.
        await command.ExecuteNonQueryAsync();
        if (online) _offline = false;
    }
}
