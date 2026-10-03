using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
                using var scope = _serviceScopeFactory.CreateScope();

                var resumer = scope.ServiceProvider
                    .GetRequiredService<IWorkflowResumer>();

                await resumer.ProcessOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed processing waiting workflows.");
            }

            try
            {
                await Task.Delay(_options.PollingInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}