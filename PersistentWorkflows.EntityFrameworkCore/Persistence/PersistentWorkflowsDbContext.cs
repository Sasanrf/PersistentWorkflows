using Microsoft.EntityFrameworkCore;
using PersistentWorkflows.EntityFrameworkCore.Entities;

namespace PersistentWorkflows.EntityFrameworkCore.Persistence;

public sealed class PersistentWorkflowsDbContext : DbContext
{
    public PersistentWorkflowsDbContext(DbContextOptions<PersistentWorkflowsDbContext> options)
        : base(options)
    {
    }

    public DbSet<WorkflowInstanceEntity> WorkflowInstances => Set<WorkflowInstanceEntity>();

    public DbSet<WorkflowStepExecutionEntity> WorkflowStepExecutions => Set<WorkflowStepExecutionEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureWorkflowInstance(modelBuilder);
        ConfigureWorkflowStepExecution(modelBuilder);
    }

    private static void ConfigureWorkflowInstance(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<WorkflowInstanceEntity>();

        entity.ToTable("WorkflowInstances", "PersistentWorkflows");

        entity.HasKey(x => x.Id);

        entity.Property(x => x.WorkflowName)
            .IsRequired()
            .HasMaxLength(200);

        entity.Property(x => x.InstanceKey)
            .IsRequired()
            .HasMaxLength(200);

        entity.Property(x => x.ContextJson)
            .IsRequired();

        entity.Property(x => x.Status)
            .IsRequired()
            .HasMaxLength(50);

        entity.Property(x => x.Version)
            .IsConcurrencyToken();

        entity.HasIndex(x => new { x.WorkflowName, x.InstanceKey })
            .IsUnique();

        entity.HasIndex(x => x.NextExecutionAtUtc);
    }

    private static void ConfigureWorkflowStepExecution(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<WorkflowStepExecutionEntity>();

        entity.ToTable("WorkflowStepExecutions", "PersistentWorkflows");

        entity.HasKey(x => x.Id);

        entity.Property(x => x.StepName)
            .IsRequired()
            .HasMaxLength(200);

        entity.Property(x => x.Status)
            .IsRequired()
            .HasMaxLength(50);

        entity.Property(x => x.ErrorCode)
            .HasMaxLength(100);

        entity.Property(x => x.ErrorMessage)
            .HasMaxLength(4000);

        entity.HasIndex(x => new { x.WorkflowInstanceId, x.StepName, x.Attempt })
            .IsUnique();

        entity.HasOne(x => x.WorkflowInstance)
            .WithMany(x => x.StepExecutions)
            .HasForeignKey(x => x.WorkflowInstanceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}