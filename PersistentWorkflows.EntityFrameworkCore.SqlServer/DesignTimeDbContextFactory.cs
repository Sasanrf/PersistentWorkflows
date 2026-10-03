using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using PersistentWorkflows.EntityFrameworkCore.Persistence;

namespace PersistentWorkflows.EntityFrameworkCore.SqlServer;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<PersistentWorkflowsDbContext>
{
    public PersistentWorkflowsDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<PersistentWorkflowsDbContext>()
        .UseSqlServer("Server=localhost;Database=PersistentWorkflowsDesign;Trusted_Connection=True;TrustServerCertificate=True",
            sql => sql.MigrationsAssembly(typeof(DesignTimeDbContextFactory).Assembly.GetName().Name))
        .Options);
}
