using Microsoft.Extensions.DependencyInjection;
using PersistentWorkflows.Abstractions.Definitions;
using PersistentWorkflows.Abstractions.Enums;
using PersistentWorkflows.Abstractions.Execution;
using PersistentWorkflows.Abstractions.Persistence;
using PersistentWorkflows.Abstractions.Results;
using PersistentWorkflows.Abstractions.Serialization;
using PersistentWorkflows.Core.Constants;
using PersistentWorkflows.Core.Registry;

namespace PersistentWorkflows.Core.Execution;

internal sealed class WorkflowRunner : IWorkflowRunner
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IWorkflowDefinitionRegistry _registry;
    private readonly IWorkflowRegistryInitializer _initializer;
    private readonly IWorkflowInstanceRepository _workflowInstanceRepository;
    private readonly IWorkflowStepExecutionRepository _workflowStepExecutionRepository;
    private readonly IWorkflowContextSerializer _workflowContextSerializer;

    public WorkflowRunner(
        IServiceProvider serviceProvider,
        IWorkflowDefinitionRegistry registry,
        IWorkflowRegistryInitializer initializer,
        IWorkflowInstanceRepository workflowInstanceRepository,
        IWorkflowStepExecutionRepository workflowStepExecutionRepository,
        IWorkflowContextSerializer workflowContextSerializer)
    {
        _serviceProvider = serviceProvider;
        _registry = registry;
        _initializer = initializer;
        _workflowInstanceRepository = workflowInstanceRepository;
        _workflowStepExecutionRepository = workflowStepExecutionRepository;
        _workflowContextSerializer = workflowContextSerializer;
    }

    public async Task<WorkflowStartResult> StartAsync<TWorkflow, TContext>(
        string instanceKey,
        TContext context,
        CancellationToken cancellationToken = default)
        where TWorkflow : class, IWorkflowDefinition<TContext>
        where TContext : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceKey);
        ArgumentNullException.ThrowIfNull(context);

        _initializer.Initialize();

        var descriptor = _registry.Get(typeof(TWorkflow));

        var existingInstance = await _workflowInstanceRepository.GetByWorkflowNameAndInstanceKeyAsync(
            descriptor.Name,
            instanceKey,
            cancellationToken);

        if (existingInstance is not null)
        {
            return new WorkflowStartResult
            {
                WorkflowInstanceId = existingInstance.Id,
                AlreadyExists = true
            };
        }

        var now = DateTime.UtcNow;

        var instance = new WorkflowInstanceState
        {
            Id = Guid.NewGuid(),
            WorkflowName = descriptor.Name,
            InstanceKey = instanceKey,
            ContextJson = _workflowContextSerializer.Serialize(context),
            Status = WorkflowStatuses.Running,
            CurrentStepIndex = 0,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            NextExecutionAtUtc = null,
            Version = 1
        };

        try
        {
            await _workflowInstanceRepository.AddAsync(instance, cancellationToken);
        }
        catch (Exception ex) when (IsUniqueConstraintViolation(ex))
        {
            var existingAfterConflict = await _workflowInstanceRepository.GetByWorkflowNameAndInstanceKeyAsync(
                descriptor.Name,
                instanceKey,
                cancellationToken);

            if (existingAfterConflict is null)
            {
                throw;
            }

            return new WorkflowStartResult
            {
                WorkflowInstanceId = existingAfterConflict.Id,
                AlreadyExists = true
            };
        }

        await ExecuteAsync<TContext>(instance, descriptor, cancellationToken);

        return new WorkflowStartResult
        {
            WorkflowInstanceId = instance.Id,
            AlreadyExists = false
        };
    }

    public async Task ResumeAsync(Guid workflowInstanceId, CancellationToken cancellationToken = default)
    {
        var instance = await _workflowInstanceRepository.GetByIdAsync(
            workflowInstanceId,
            cancellationToken);

        if (instance is null)
        {
            throw new InvalidOperationException(
                $"Workflow instance '{workflowInstanceId}' was not found.");
        }

        if (instance.Status == WorkflowStatuses.Succeeded)
        {
            return;
        }

        if (instance.Status == WorkflowStatuses.Failed)
        {
            return;
        }

        if (instance.Status == WorkflowStatuses.Waiting)
        {
            instance.Status = WorkflowStatuses.Running;
            instance.UpdatedAtUtc = DateTime.UtcNow;
            instance.NextExecutionAtUtc = null;

            await _workflowInstanceRepository.UpdateAsync(instance, cancellationToken);
        }

        var descriptor = _registry.GetByName(instance.WorkflowName);

        await InvokeExecuteAsync(instance, descriptor, cancellationToken);
    }

    private async Task ExecuteAsync<TContext>(
        WorkflowInstanceState instance,
        Definitions.WorkflowDefinitionDescriptor descriptor,
        CancellationToken cancellationToken)
        where TContext : class
    {
        while (instance.Status == WorkflowStatuses.Running)
        {
            if (instance.CurrentStepIndex >= descriptor.Steps.Count)
            {
                instance.Status = WorkflowStatuses.Succeeded;
                instance.UpdatedAtUtc = DateTime.UtcNow;

                await _workflowInstanceRepository.UpdateAsync(instance, cancellationToken);
                return;
            }

            var stepDefinition = descriptor.Steps[instance.CurrentStepIndex];
            var attempt = await _workflowStepExecutionRepository.GetNextAttemptAsync(
                instance.Id,
                stepDefinition.Name,
                cancellationToken);

            var startedAtUtc = DateTime.UtcNow;

            var stepExecution = new WorkflowStepExecutionState
            {
                Id = Guid.NewGuid(),
                WorkflowInstanceId = instance.Id,
                StepName = stepDefinition.Name,
                Status = StepExecutionStatuses.Started,
                Attempt = attempt,
                StartedAtUtc = startedAtUtc,
                CompletedAtUtc = null,
                ErrorCode = null,
                ErrorMessage = null
            };

            await _workflowStepExecutionRepository.AddAsync(stepExecution, cancellationToken);

            using var scope = _serviceProvider.CreateScope();

            var step = scope.ServiceProvider.GetRequiredService(stepDefinition.StepType);

            var context = _workflowContextSerializer.Deserialize<TContext>(instance.ContextJson);

            var executionContext = new WorkflowExecutionContext<TContext>
            {
                WorkflowInstanceId = instance.Id,
                WorkflowName = instance.WorkflowName,
                InstanceKey = instance.InstanceKey,
                StepName = stepDefinition.Name,
                Attempt = attempt,
                Data = context,
                Services = scope.ServiceProvider
            };

            var result = await InvokeStepAsync(
                step,
                stepDefinition.StepType,
                executionContext,
                cancellationToken);

            stepExecution.CompletedAtUtc = DateTime.UtcNow;
            instance.ContextJson = _workflowContextSerializer.Serialize(executionContext.Data);
            instance.UpdatedAtUtc = DateTime.UtcNow;

            switch (result.Status)
            {
                case StepExecutionStatus.Succeeded:
                    stepExecution.Status = StepExecutionStatuses.Succeeded;
                    instance.CurrentStepIndex++;
                    break;

                case StepExecutionStatus.Waiting:
                    stepExecution.Status = StepExecutionStatuses.Waiting;
                    stepExecution.ErrorCode = result.ErrorCode;
                    stepExecution.ErrorMessage = result.ErrorMessage;

                    instance.Status = WorkflowStatuses.Waiting;
                    instance.NextExecutionAtUtc = result.RetryAfter.HasValue
                        ? DateTime.UtcNow.Add(result.RetryAfter.Value)
                        : null;
                    break;

                case StepExecutionStatus.RetryableFailure:
                    stepExecution.Status = StepExecutionStatuses.RetryableFailure;
                    stepExecution.ErrorCode = result.ErrorCode;
                    stepExecution.ErrorMessage = result.ErrorMessage;

                    instance.Status = WorkflowStatuses.Waiting;
                    instance.NextExecutionAtUtc = result.RetryAfter.HasValue
                        ? DateTime.UtcNow.Add(result.RetryAfter.Value)
                        : null;
                    break;

                case StepExecutionStatus.Failed:
                    stepExecution.Status = StepExecutionStatuses.Failed;
                    stepExecution.ErrorCode = result.ErrorCode;
                    stepExecution.ErrorMessage = result.ErrorMessage;

                    instance.Status = WorkflowStatuses.Failed;
                    instance.NextExecutionAtUtc = null;
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Unsupported step result status '{result.Status}'.");
            }

            await _workflowStepExecutionRepository.UpdateAsync(stepExecution, cancellationToken);
            await _workflowInstanceRepository.UpdateAsync(instance, cancellationToken);

            if (instance.Status != WorkflowStatuses.Running)
            {
                return;
            }
        }
    }

    private static bool IsUniqueConstraintViolation(Exception exception)
    {
        var current = exception;

        while (current is not null)
        {
            var message = current.Message;

            if (!string.IsNullOrWhiteSpace(message) &&
                (message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase) ||
                 message.Contains("duplicate", StringComparison.OrdinalIgnoreCase) ||
                 message.Contains("unique", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            current = current.InnerException!;
        }

        return false;
    }

    private async Task<StepResult> InvokeStepAsync<TContext>(
        object step,
        Type stepType,
        WorkflowExecutionContext<TContext> executionContext,
        CancellationToken cancellationToken)
        where TContext : class
    {
        var method = stepType.GetMethod(nameof(IWorkflowStep<TContext>.ExecuteAsync))
                     ?? throw new InvalidOperationException(
                         $"Step '{stepType.FullName}' does not define ExecuteAsync.");

        return await (Task<StepResult>)method.Invoke(
            step,
            new object[] { executionContext, cancellationToken })!;
    }

    private async Task InvokeExecuteAsync(
        WorkflowInstanceState instance,
        Definitions.WorkflowDefinitionDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        var method = typeof(WorkflowRunner).GetMethod(
            nameof(ExecuteAsync),
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        if (method is null)
        {
            throw new InvalidOperationException("ExecuteAsync method was not found.");
        }

        var genericMethod = method.MakeGenericMethod(descriptor.ContextType);

        await (Task)genericMethod.Invoke(this, new object[] { instance, descriptor, cancellationToken })!;
    }

}