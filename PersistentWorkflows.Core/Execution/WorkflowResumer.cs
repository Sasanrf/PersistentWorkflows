using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersistentWorkflows.Abstractions.Execution;
using PersistentWorkflows.Abstractions.Persistence;
using PersistentWorkflows.Core.DependencyInjection;

namespace PersistentWorkflows.Core.Execution;

internal sealed class WorkflowResumer : IWorkflowResumer
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<WorkflowResumer> _logger;
    private readonly PersistentWorkflowsWorkerOptions _options;

    public WorkflowResumer(
        IServiceScopeFactory serviceScopeFactory,
        ILogger<WorkflowResumer> logger,
        IOptions<PersistentWorkflowsWorkerOptions> options)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
        _options = options.Value;
    }

    public async Task ProcessOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceScopeFactory.CreateScope();

        var repository = scope.ServiceProvider.GetRequiredService<IWorkflowInstanceRepository>();
        var runner = scope.ServiceProvider.GetRequiredService<IWorkflowRunner>();

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
                    "Failed to resume workflow instance {WorkflowInstanceId}",
                    workflow.Id);
            }
        }
    }
}