using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PersistentWorkflows.Abstractions.Persistence;
using PersistentWorkflows.EntityFrameworkCore.Persistence;
using Xunit;
using PersistentWorkflows.EntityFrameworkCore.Entities;

namespace PersistentWorkflows.EntityFrameworkCore.Tests.Persistence;

public sealed class WorkflowInstanceRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public WorkflowInstanceRepositoryTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    [Fact]
    public async Task UpdateAsync_Should_Throw_When_Instance_Was_Updated_By_Another_Process()
    {
        var options = new DbContextOptionsBuilder<PersistentWorkflowsDbContext>()
            .UseSqlite(_connection)
            .Options;

        await using (var setupContext = new PersistentWorkflowsDbContext(options))
        {
            await setupContext.Database.EnsureCreatedAsync();

            setupContext.WorkflowInstances.Add(new EntityFrameworkCore.Entities.WorkflowInstanceEntity
            {
                Id = Guid.NewGuid(),
                WorkflowName = "test-workflow",
                InstanceKey = "key-1",
                ContextJson = "{}",
                Status = "Running",
                CurrentStepIndex = 0,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                Version = 1
            });

            await setupContext.SaveChangesAsync();
        }

        WorkflowInstanceState fromFirstContext;
        WorkflowInstanceState fromSecondContext;

        await using (var firstContext = new PersistentWorkflowsDbContext(options))
        await using (var secondContext = new PersistentWorkflowsDbContext(options))
        {
            var firstRepo = new WorkflowInstanceRepository(firstContext);
            var secondRepo = new WorkflowInstanceRepository(secondContext);

            fromFirstContext = (await firstRepo.GetByWorkflowNameAndInstanceKeyAsync(
                "test-workflow",
                "key-1",
                CancellationToken.None))!;

            fromSecondContext = (await secondRepo.GetByWorkflowNameAndInstanceKeyAsync(
                "test-workflow",
                "key-1",
                CancellationToken.None))!;

            fromFirstContext.Status = "Waiting";
            fromFirstContext.UpdatedAtUtc = DateTime.UtcNow;

            await firstRepo.UpdateAsync(fromFirstContext, CancellationToken.None);

            fromSecondContext.Status = "Failed";
            fromSecondContext.UpdatedAtUtc = DateTime.UtcNow;

            var action = async () => await secondRepo.UpdateAsync(fromSecondContext, CancellationToken.None);

            await action.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*updated by another process*");
        }
    }

    [Fact]
    public async Task GetWaitingForResumeAsync_Should_Return_Only_Due_Waiting_Workflows()
    {
        var options = new DbContextOptionsBuilder<PersistentWorkflowsDbContext>()
            .UseSqlite(_connection)
            .Options;

        var now = DateTime.UtcNow;

        await using (var setupContext = new PersistentWorkflowsDbContext(options))
        {
            await setupContext.Database.EnsureCreatedAsync();

            setupContext.WorkflowInstances.AddRange(
                CreateInstance("due-1", "Waiting", now.AddMinutes(-10)),
                CreateInstance("due-2", "Waiting", now),
                CreateInstance("future", "Waiting", now.AddMinutes(10)),
                CreateInstance("running", "Running", now.AddMinutes(-10)),
                CreateInstance("failed", "Failed", now.AddMinutes(-10)),
                CreateInstance("succeeded", "Succeeded", now.AddMinutes(-10)),
                CreateInstance("waiting-no-date", "Waiting", null));

            await setupContext.SaveChangesAsync();
        }

        await using var dbContext = new PersistentWorkflowsDbContext(options);
        var repository = new WorkflowInstanceRepository(dbContext);

        var result = await repository.GetWaitingForResumeAsync(now, CancellationToken.None);

        result.Should().HaveCount(2);
        result.Select(x => x.InstanceKey).Should().Equal("due-1", "due-2");
    }

    private static WorkflowInstanceEntity CreateInstance(
        string instanceKey,
        string status,
        DateTime? nextExecutionAtUtc)
    {
        return new WorkflowInstanceEntity
        {
            Id = Guid.NewGuid(),
            WorkflowName = "test-workflow",
            InstanceKey = instanceKey,
            ContextJson = "{}",
            Status = status,
            CurrentStepIndex = 0,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            NextExecutionAtUtc = nextExecutionAtUtc,
            Version = 1
        };
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}