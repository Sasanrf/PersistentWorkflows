sequenceDiagram
    autonumber

    participant Consumer as Application
    participant IWR as IWorkflowRunner
    participant Runner as WorkflowRunner
    participant Initializer as WorkflowRegistryInitializer
    participant Factory as IWorkflowDefinitionFactory
    participant Registry as WorkflowDefinitionRegistry
    participant Serializer as IWorkflowContextSerializer
    participant InstRepo as IWorkflowInstanceRepository
    participant StepRepo as IWorkflowStepExecutionRepository
    participant SP as IServiceProvider
    participant Step as WorkflowStep
    participant DB as Database

    note over Consumer,DB: StartAsync Flow

    Consumer->>IWR: StartAsync(instanceKey, context)
    IWR->>Runner: StartAsync(instanceKey, context)

    Runner->>Initializer: Initialize()
    Initializer->>Factory: Create(workflowType, contextType)
    Factory-->>Initializer: WorkflowDefinitionDescriptor
    Initializer->>Registry: Register(descriptor)

    Runner->>Registry: Get(workflowType)
    Registry-->>Runner: WorkflowDefinitionDescriptor

    Runner->>InstRepo: GetByWorkflowNameAndInstanceKeyAsync(name, instanceKey)
    InstRepo->>DB: SELECT WorkflowInstance
    DB-->>InstRepo: row or null
    InstRepo-->>Runner: existing instance or null

    alt Instance already exists
        Runner-->>Consumer: AlreadyExists = true
    else New instance
        Runner->>Serializer: Serialize(context)
        Serializer-->>Runner: contextJson

        Runner->>InstRepo: AddAsync(instance)
        InstRepo->>DB: INSERT WorkflowInstance
        DB-->>InstRepo: ok or unique constraint violation

        alt Unique constraint race condition
            Runner->>InstRepo: GetByWorkflowNameAndInstanceKeyAsync(name, instanceKey)
            InstRepo-->>Runner: winning instance
            Runner-->>Consumer: AlreadyExists = true
        else Insert succeeded
            note over Runner,DB: ExecuteAsync loop

            loop For each step while instance is Running
                Runner->>StepRepo: GetNextAttemptAsync(instanceId, stepName)
                StepRepo->>DB: SELECT MAX Attempt
                DB-->>StepRepo: attempt number
                StepRepo-->>Runner: attempt

                Runner->>StepRepo: AddAsync(step execution started)
                StepRepo->>DB: INSERT WorkflowStepExecution

                Runner->>SP: CreateScope and resolve step
                SP-->>Runner: WorkflowStep instance

                Runner->>Serializer: Deserialize(contextJson)
                Serializer-->>Runner: context data

                Runner->>Step: ExecuteAsync(executionContext, cancellationToken)
                Step-->>Runner: StepResult

                Runner->>Serializer: Serialize(updatedData)
                Serializer-->>Runner: updated contextJson

                alt Step succeeded
                    note right of Runner: Move to next step
                else Step waiting or retryable failure
                    note right of Runner: Set status Waiting and NextExecutionAtUtc
                else Step failed
                    note right of Runner: Set status Failed
                end

                Runner->>StepRepo: UpdateAsync(step execution)
                StepRepo->>DB: UPDATE WorkflowStepExecution

                Runner->>InstRepo: UpdateAsync(instance)
                InstRepo->>DB: UPDATE WorkflowInstance
            end

            alt All steps completed
                Runner->>InstRepo: UpdateAsync(status Succeeded)
                InstRepo->>DB: UPDATE WorkflowInstance
            end

            Runner-->>Consumer: AlreadyExists = false
        end
    end

    note over Consumer,DB: ResumeAsync Flow

    Consumer->>IWR: ResumeAsync(workflowInstanceId)
    IWR->>Runner: ResumeAsync(workflowInstanceId)

    Runner->>InstRepo: GetByIdAsync(workflowInstanceId)
    InstRepo->>DB: SELECT WorkflowInstance
    DB-->>InstRepo: row or null
    InstRepo-->>Runner: WorkflowInstanceState

    alt Instance is Succeeded or Failed
        Runner-->>Consumer: return no-op
    else Instance is Waiting
        Runner->>InstRepo: UpdateAsync(status Running)
        InstRepo->>DB: UPDATE WorkflowInstance

        Runner->>Registry: GetByName(workflowName)
        Registry-->>Runner: WorkflowDefinitionDescriptor

        note over Runner,DB: Resume execution from CurrentStepIndex

        Runner->>Step: ExecuteAsync(executionContext, cancellationToken)
        Step-->>Runner: StepResult

        Runner-->>Consumer: return completed or waiting again
    end