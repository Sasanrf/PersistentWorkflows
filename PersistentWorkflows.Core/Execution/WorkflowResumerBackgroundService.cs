using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersistentWorkflows.Abstractions.Execution;
using PersistentWorkflows.Abstractions.Persistence;
using PersistentWorkflows.Core.DependencyInjection;

namespace PersistentWorkflows.Core.Execution;

internal sealed class WorkflowResumerBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<WorkflowResumerBackgroundService> _logger;
    private readonly PersistentWorkflowsWorkerOptions _options;

    public WorkflowResumerBackgroundService(
        IServiceScopeFactory serviceScopeFactory,
        ILogger<WorkflowResumerBackgroundService> logger,
        IOptions<PersistentWorkflowsWorkerOptions> options)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessWaitingWorkflows(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed processing waiting workflows.");
            }

            await Task.Delay(_options.PollingInterval, stoppingToken);
        }
    }

    private async Task ProcessWaitingWorkflows(CancellationToken cancellationToken)
    {
        using var scope = _serviceScopeFactory.CreateScope();

        var repository = scope.ServiceProvider
            .GetRequiredService<IWorkflowInstanceRepository>();

        var runner = scope.ServiceProvider
            .GetRequiredService<IWorkflowRunner>();

        var workflows = await repository.GetWaitingForResumeAsync(
            DateTime.UtcNow,
            cancellationToken);

        foreach (var workflow in workflows.Take(_options.BatchSize))
        {
            try
            {
                await runner.ResumeAsync(workflow.Id, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed resuming workflow instance {WorkflowInstanceId}",
                    workflow.Id);
            }
        }
    }
}