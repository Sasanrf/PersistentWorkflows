using Microsoft.EntityFrameworkCore;
using PersistentWorkflows.Abstractions.Persistence;
using PersistentWorkflows.EntityFrameworkCore.Persistence;
using Xunit;

namespace PersistentWorkflows.EntityFrameworkCore.Tests.Execution;

public sealed class SqliteWorkflowStoreTests : WorkflowStoreConformanceTests, IAsyncLifetime
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"workflow-tests-{Guid.NewGuid():N}.db");
    private DbContextOptions<PersistentWorkflowsDbContext> Options => new DbContextOptionsBuilder<PersistentWorkflowsDbContext>().UseSqlite($"Data Source={_path};Pooling=False").Options;
    protected override IWorkflowStore CreateStore() => new EfWorkflowStore(Options);
    public async ValueTask InitializeAsync()
    {
        await using var db = new PersistentWorkflowsDbContext(Options);
        await db.Database.EnsureCreatedAsync();
    }
    public ValueTask DisposeAsync() { File.Delete(_path); return ValueTask.CompletedTask; }
}
