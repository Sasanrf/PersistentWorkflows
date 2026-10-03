using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersistentWorkflows.Abstractions.Definitions;
using PersistentWorkflows.Abstractions.Enums;
using PersistentWorkflows.Abstractions.Execution;
using PersistentWorkflows.Abstractions.Persistence;
using PersistentWorkflows.Abstractions.Results;
using PersistentWorkflows.Abstractions.Serialization;
using PersistentWorkflows.Core.Definitions;
using PersistentWorkflows.Core.DependencyInjection;
using PersistentWorkflows.Core.Registry;

namespace PersistentWorkflows.Core.Execution;

internal interface IWorkflowProcessor
{
    Task ProcessAsync(Guid id, bool force, CancellationToken cancellationToken);
}

internal sealed class WorkflowRunner(
    IServiceScopeFactory scopes, IWorkflowDefinitionRegistry registry, IWorkflowRegistryInitializer initializer,
    IWorkflowStore store, IWorkflowContextSerializer serializer, IWorkflowContextMigrator migrator,
    IOptions<PersistentWorkflowsOptions> options, TimeProvider clock, ILogger<WorkflowRunner> logger) : IWorkflowRunner, IWorkflowProcessor
{
    private readonly PersistentWorkflowsOptions _options = options.Value;
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<WorkflowStartResult> EnqueueAsync<TWorkflow, TContext>(string instanceKey, TContext context, CancellationToken cancellationToken = default)
        where TWorkflow : class, IWorkflowDefinition<TContext> where TContext : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceKey);
        if (instanceKey.Length > 200) throw new ArgumentOutOfRangeException(nameof(instanceKey));
        ArgumentNullException.ThrowIfNull(context);
        await initializer.InitializeAsync(cancellationToken);
        var definition = registry.Get(typeof(TWorkflow));
        if (definition.ContextType != typeof(TContext)) throw new InvalidOperationException("The requested context type differs from the registered workflow context.");
        var json = serializer.Serialize(context);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        var now = Now;
        var (instance, created) = await store.CreateAsync(new WorkflowInstanceState
        {
            Id = Guid.NewGuid(), WorkflowName = definition.Name, InstanceKey = instanceKey,
            ContextJson = json, InputHash = hash, DefinitionHash = definition.Fingerprint,
            DefinitionVersion = definition.Version, ContextSchemaVersion = definition.ContextSchemaVersion,
            Status = "Pending", CurrentStepIndex = 0, CurrentStepName = definition.Steps[0].Name,
            CreatedAtUtc = now, UpdatedAtUtc = now, Version = 1
        }, cancellationToken);
        if (!created && instance.InputHash.Length != 0 && instance.InputHash != hash)
            throw new WorkflowInputConflictException("The workflow business key already exists with different input.");
        if (created) WorkflowDiagnostics.Accepted.Add(1);
        return Result(instance, !created);
    }

    public async Task<WorkflowStartResult> StartAsync<TWorkflow, TContext>(string instanceKey, TContext context, CancellationToken cancellationToken = default)
        where TWorkflow : class, IWorkflowDefinition<TContext> where TContext : class
    {
        var result = await EnqueueAsync<TWorkflow, TContext>(instanceKey, context, cancellationToken);
        if (result.WasCreated) await ProcessAsync(result.WorkflowInstanceId, false, cancellationToken);
        return Result((await store.GetAsync(result.WorkflowInstanceId, cancellationToken))!, result.AlreadyExists);
    }
    private static WorkflowStartResult Result(WorkflowInstanceState state, bool exists) => new()
    {
        WorkflowInstanceId = state.Id, AlreadyExists = exists, Status = Enum.Parse<WorkflowStatus>(state.Status)
    };

    public async Task ResumeAsync(Guid workflowInstanceId, CancellationToken cancellationToken = default)
    {
        if (await store.GetAsync(workflowInstanceId, cancellationToken) is null)
            throw new InvalidOperationException($"Workflow instance '{workflowInstanceId}' was not found.");
        await ProcessAsync(workflowInstanceId, true, cancellationToken);
    }

    public async Task ProcessAsync(Guid id, bool force, CancellationToken cancellationToken)
    {
        await initializer.InitializeAsync(cancellationToken);
        var token = Guid.NewGuid();
        var state = await store.TryClaimAsync(id, token, Now, _options.LeaseDuration, force, cancellationToken);
        if (state is null) return;
        using var activity = WorkflowDiagnostics.ActivitySource.StartActivity("workflow.execute");
        activity?.SetTag("workflow.id", id).SetTag("workflow.name", state.WorkflowName).SetTag("workflow.version", state.DefinitionVersion);
        using var logScope = logger.BeginScope(new Dictionary<string, object> { ["WorkflowInstanceId"] = id, ["WorkflowName"] = state.WorkflowName });
        using var executionStop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var heartbeatStop = new CancellationTokenSource();
        var lease = new LeaseMonitor();
        var heartbeat = RenewAsync(id, token, lease, executionStop, heartbeatStop.Token);
        try
        {
            WorkflowDefinitionDescriptor definition;
            try
            {
                definition = registry.GetByName(state.WorkflowName, state.DefinitionVersion);
                if (state.DefinitionHash.Length != 0 && state.DefinitionHash != definition.Fingerprint)
                    throw new InvalidOperationException("The persisted workflow definition changed without a new version.");
                if (state.CurrentStepIndex < 0 || state.CurrentStepIndex > definition.Steps.Count ||
                    (state.CurrentStepIndex < definition.Steps.Count && state.CurrentStepName is not null && state.CurrentStepName != definition.Steps[state.CurrentStepIndex].Name))
                    throw new InvalidOperationException("The persisted workflow cursor does not match the registered definition.");
                if (state.ContextSchemaVersion > definition.ContextSchemaVersion)
                    throw new InvalidOperationException("Context schema downgrade is not supported.");
                if (state.ContextSchemaVersion != definition.ContextSchemaVersion)
                    state.ContextJson = migrator.Migrate(state.WorkflowName, state.DefinitionVersion, state.ContextSchemaVersion, definition.ContextSchemaVersion, state.ContextJson);
                state.ContextSchemaVersion = definition.ContextSchemaVersion;
                state.DefinitionHash = definition.Fingerprint;
            }
            catch (Exception ex)
            {
                state.Status = "Failed";
                state.NextExecutionAtUtc = null;
                state.WaitingSignal = null;
                state.LastErrorCode = "DefinitionUnavailable";
                state.LastErrorMessage = Truncate(ex.Message, 4000);
                await CommitAsync(state, token, null, cancellationToken);
                logger.LogError(ex, "Workflow definition or context could not be loaded");
                return;
            }
            while (state.Status == "Running")
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (lease.Lost) throw new WorkflowLeaseLostException();
                var latest = await store.GetAsync(id, cancellationToken) ?? throw new WorkflowLeaseLostException();
                if (latest.CancellationRequested || state.CancellationRequested)
                {
                    state.Status = "Cancelled";
                    await CommitAsync(state, token, null, cancellationToken);
                    return;
                }
                if (state.CurrentStepIndex == definition.Steps.Count)
                {
                    state.Status = "Succeeded";
                    await CommitAsync(state, token, null, cancellationToken);
                    return;
                }
                var step = definition.Steps[state.CurrentStepIndex];
                var execution = await store.BeginStepAsync(id, token, step.Name, Now, cancellationToken);
                using var stepActivity = WorkflowDiagnostics.ActivitySource.StartActivity("workflow.step");
                stepActivity?.SetTag("workflow.step", step.Name).SetTag("workflow.attempt", execution.Attempt);
                var started = clock.GetTimestamp();
                StepInvocation invocation;
                using var stepStop = CancellationTokenSource.CreateLinkedTokenSource(executionStop.Token);
                using var timeout = clock.CreateTimer(_ => CancelSafely(stepStop), null, _options.StepTimeout, Timeout.InfiniteTimeSpan);
                var scope = scopes.CreateAsyncScope();
                Task<StepInvocation>? action = null;
                try
                {
                    var payload = state.WaitingSignal is null ? null : (await store.GetSignalAsync(id, state.WaitingSignal, cancellationToken))?.PayloadJson;
                    action = step.InvokeAsync!(scope.ServiceProvider, state, serializer, execution.Attempt, payload, stepStop.Token);
                    invocation = await action.WaitAsync(stepStop.Token);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException) when (lease.Lost)
                {
                    throw new WorkflowLeaseLostException();
                }
                catch (OperationCanceledException)
                {
                    if ((await store.GetAsync(id, cancellationToken))?.CancellationRequested == true)
                        invocation = new(StepResult.Fail("Cancelled", "Cancellation requested."), state.ContextJson);
                    else
                        invocation = new(new StepResult { Status = StepExecutionStatus.RetryableFailure, ErrorCode = "StepTimeout", ErrorMessage = "Action timed out." }, state.ContextJson);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Action {StepName} attempt {Attempt} failed", step.Name, execution.Attempt);
                    invocation = new(_options.IsRetryableException(ex) ? StepResult.Retry(Truncate(ex.Message, 4000)) : StepResult.Fail("UnhandledException", Truncate(ex.Message, 4000)), state.ContextJson);
                }
                finally
                {
                    if (action is { IsCompleted: false }) _ = DisposeAfterActionAsync(action, scope);
                    else await scope.DisposeAsync();
                    WorkflowDiagnostics.StepDuration.Record(clock.GetElapsedTime(started).TotalMilliseconds);
                }
                if (lease.Lost) throw new WorkflowLeaseLostException();
                ApplyOutcome(state, execution, invocation, definition);
                await CommitAsync(state, token, execution, cancellationToken);
                state = (await store.GetAsync(id, cancellationToken))!;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await ReleaseAsync(id, token);
            throw;
        }
        catch (WorkflowLeaseLostException)
        {
            WorkflowDiagnostics.LeaseLosses.Add(1);
            logger.LogWarning("Workflow ownership was lost; progress was not committed");
            await ReleaseAsync(id, token);
        }
        catch
        {
            await ReleaseAsync(id, token);
            throw;
        }
        finally
        {
            heartbeatStop.Cancel();
            await heartbeat;
        }
    }

    private void ApplyOutcome(WorkflowInstanceState state, WorkflowStepExecutionState execution, StepInvocation invocation, WorkflowDefinitionDescriptor definition)
    {
        var result = invocation.Result;
        if (result is null || !Enum.IsDefined(result.Status) || result.RetryAfter < TimeSpan.Zero ||
            result.RetryAfter > DateTime.MaxValue - Now || result.SignalName?.Length > 200 ||
            (result.SignalName is not null && (string.IsNullOrWhiteSpace(result.SignalName) || result.Status != StepExecutionStatus.Waiting)))
            result = StepResult.Fail("InvalidStepResult", "The action returned an invalid outcome.");
        state.ContextJson = invocation.ContextJson;
        execution.CompletedAtUtc = Now;
        execution.ErrorCode = Truncate(result.ErrorCode, 100);
        execution.ErrorMessage = Truncate(result.ErrorMessage, 4000);
        state.LastErrorCode = execution.ErrorCode;
        state.LastErrorMessage = execution.ErrorMessage;
        state.NextExecutionAtUtc = null;
        state.WaitingSignal = null;
        switch (result.Status)
        {
            case StepExecutionStatus.Succeeded:
                execution.Status = "Succeeded";
                state.ConsecutiveFailures = 0;
                state.CurrentStepIndex++;
                state.CurrentStepName = state.CurrentStepIndex < definition.Steps.Count ? definition.Steps[state.CurrentStepIndex].Name : null;
                state.Status = state.CurrentStepIndex == definition.Steps.Count ? "Succeeded" : "Running";
                break;
            case StepExecutionStatus.Waiting:
                execution.Status = "Waiting";
                state.ConsecutiveFailures = 0;
                state.Status = "Waiting";
                state.WaitingSignal = result.SignalName;
                state.NextExecutionAtUtc = result.RetryAfter.HasValue ? Now.Add(result.RetryAfter.Value) : null;
                break;
            case StepExecutionStatus.RetryableFailure:
                execution.Status = "RetryableFailure";
                state.ConsecutiveFailures++;
                state.Status = state.ConsecutiveFailures >= _options.MaximumAttempts ? "Failed" : "Waiting";
                if (state.Status == "Waiting")
                {
                    var delay = result.RetryAfter ?? RetryDelay(state.ConsecutiveFailures);
                    state.NextExecutionAtUtc = Now.Add(delay);
                    WorkflowDiagnostics.Retries.Add(1);
                }
                else state.LastErrorCode = "RetriesExhausted";
                break;
            case StepExecutionStatus.Failed:
                execution.Status = result.ErrorCode == "Cancelled" ? "Cancelled" : "Failed";
                state.Status = execution.Status;
                break;
        }
    }
    private TimeSpan RetryDelay(int failures)
    {
        var milliseconds = Math.Min(_options.MaximumRetryDelay.TotalMilliseconds, _options.InitialRetryDelay.TotalMilliseconds * Math.Pow(2, Math.Min(failures - 1, 30)));
        milliseconds *= 1 + (Random.Shared.NextDouble() * 2 - 1) * _options.RetryJitterRatio;
        return TimeSpan.FromMilliseconds(Math.Clamp(milliseconds, 1, _options.MaximumRetryDelay.TotalMilliseconds));
    }
    private async Task CommitAsync(WorkflowInstanceState state, Guid token, WorkflowStepExecutionState? execution, CancellationToken ct)
    {
        if (!await store.CommitAsync(state, token, execution, Now, ct)) throw new WorkflowLeaseLostException();
        WorkflowDiagnostics.Outcomes.Add(1, new KeyValuePair<string, object?>("status", state.Status));
        logger.LogInformation("Workflow outcome {Status} at step {StepIndex}", state.Status, state.CurrentStepIndex);
    }
    private async Task RenewAsync(Guid id, Guid token, LeaseMonitor monitor, CancellationTokenSource executionStop, CancellationToken ct)
    {
        try
        {
            while (true)
            {
                await Task.Delay(_options.LeaseRenewalInterval, clock, ct);
                using var scope = scopes.CreateScope();
                var heartbeatStore = scope.ServiceProvider.GetRequiredService<IWorkflowStore>();
                if (!await heartbeatStore.RenewLeaseAsync(id, token, Now, _options.LeaseDuration, ct))
                {
                    monitor.Lost = (await heartbeatStore.GetAsync(id, ct))?.CancellationRequested != true;
                    CancelSafely(executionStop);
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            monitor.Lost = true;
            CancelSafely(executionStop);
            logger.LogError(ex, "Workflow lease renewal failed");
        }
    }
    private async Task ReleaseAsync(Guid id, Guid token)
    {
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try { await store.ReleaseAsync(id, token, Now, cleanup.Token); }
        catch (Exception ex) { logger.LogWarning(ex, "Could not release workflow; lease expiry will recover it"); }
    }
    private async Task DisposeAfterActionAsync(Task action, AsyncServiceScope scope)
    {
        try { await action; }
        catch (Exception ex) { logger.LogDebug(ex, "Detached action finished after cancellation"); }
        finally
        {
            try { await scope.DisposeAsync(); }
            catch (Exception ex) { logger.LogError(ex, "Failed disposing a detached action scope"); }
        }
    }
    private void CancelSafely(CancellationTokenSource source)
    {
        try { source.Cancel(); }
        catch (ObjectDisposedException) { }
        catch (AggregateException ex) { logger.LogWarning(ex, "An action cancellation callback failed"); }
    }
    private sealed class LeaseMonitor { public volatile bool Lost; }
    private static string? Truncate(string? value, int length) => value?.Length > length ? value[..length] : value;
    private static void Page(int skip, int take)
    {
        if (skip < 0 || take is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(take), "Skip must be nonnegative and take must be 1–1000.");
    }
    public Task<WorkflowInstanceState?> GetAsync(Guid id, CancellationToken cancellationToken = default) => store.GetAsync(id, cancellationToken);
    public Task<IReadOnlyList<WorkflowInstanceState>> ListAsync(WorkflowStatus? status = null, int skip = 0, int take = 100, CancellationToken cancellationToken = default)
    {
        Page(skip, take);
        if (status.HasValue && !Enum.IsDefined(status.Value)) throw new ArgumentOutOfRangeException(nameof(status));
        return store.ListAsync(status?.ToString(), skip, take, cancellationToken);
    }
    public Task<IReadOnlyList<WorkflowStepExecutionState>> GetHistoryAsync(Guid id, int skip = 0, int take = 100, CancellationToken cancellationToken = default)
    { Page(skip, take); return store.GetHistoryAsync(id, skip, take, cancellationToken); }
    public Task<bool> CancelAsync(Guid id, CancellationToken cancellationToken = default) => store.RequestCancellationAsync(id, Now, cancellationToken);
    public Task<bool> RetryAsync(Guid id, string reason, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (reason.Length > 4000) throw new ArgumentOutOfRangeException(nameof(reason));
        return store.RetryAsync(id, reason, Now, cancellationToken);
    }
    public Task<bool> SignalAsync(Guid id, string signalName, string? payloadJson = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signalName);
        if (signalName.Length > 200) throw new ArgumentOutOfRangeException(nameof(signalName));
        if (payloadJson is not null) { using var document = JsonDocument.Parse(payloadJson); }
        return store.SignalAsync(id, signalName, payloadJson, Now, cancellationToken);
    }
    public Task<int> DeleteCompletedAsync(DateTime completedBeforeUtc, int limit = 100, CancellationToken cancellationToken = default)
    {
        Page(0, limit);
        if (completedBeforeUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Retention cutoff must be UTC.", nameof(completedBeforeUtc));
        return store.DeleteCompletedAsync(completedBeforeUtc, limit, cancellationToken);
    }
}
