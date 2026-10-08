using Microsoft.EntityFrameworkCore;

namespace PersistentWorkflows.EntityFrameworkCore.Tests.Execution;

/// <summary>Run every runner durability assertion against the shipped provider and real migrations.</summary>
public sealed class SqlServerDurableWorkflowTests : DurableWorkflowTests
{
    private readonly IsolatedSqlServerDatabase _database = new();
    protected override void ConfigureDatabase(DbContextOptionsBuilder options) => _database.Configure(options);
    public override ValueTask InitializeAsync() => _database.InitializeAsync();
    public override ValueTask DisposeAsync() => _database.DisposeAsync();
}
