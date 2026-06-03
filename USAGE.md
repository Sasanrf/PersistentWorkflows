# PersistentWorkflows – Usage

## Registration

    services.AddPersistentWorkflows();

    services.AddPersistentWorkflowsEntityFrameworkCore(options =>
        options.UseSqlServer(connectionString));

    services.AddWorkflow<MyWorkflow, MyWorkflowContext>();

## Start a workflow

    await workflowRunner.StartAsync<MyWorkflow, MyWorkflowContext>(
        "business-key",
        new MyWorkflowContext());

## Resume a workflow

    await workflowRunner.ResumeAsync(workflowInstanceId);