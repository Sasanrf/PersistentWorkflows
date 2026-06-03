using PersistentWorkflows.Core.Registry;

namespace PersistentWorkflows.Core.DependencyInjection;

public sealed class PersistentWorkflowsOptions
{
    internal List<WorkflowRegistration> Registrations { get; } = [];
}