using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersistentWorkflows.Abstractions.Persistence;
using PersistentWorkflows.Core.DependencyInjection;

namespace PersistentWorkflows.Core.Execution;

internal sealed class WorkflowResumer(IServiceScopeFactory scopes, ILogger<WorkflowResumer> logger, IOptions<PersistentWorkflowsWorkerOptions> options, TimeProvider clock) : IWorkflowResumer
{
    public async Task ProcessOnceAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> ids;
        using (var scope = scopes.CreateScope())
            ids = await scope.ServiceProvider.GetRequiredService<IWorkflowStore>().GetDueAsync(clock.GetUtcNow().UtcDateTime, options.Value.BatchSize, cancellationToken);
        await Parallel.ForEachAsync(ids, new ParallelOptions { MaxDegreeOfParallelism = options.Value.MaximumConcurrency, CancellationToken = cancellationToken }, async (id, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IWorkflowProcessor>().ProcessAsync(id, false, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { logger.LogError(ex, "Failed processing workflow {WorkflowInstanceId}", id); }
        });
    }
}
