using PersistentWorkflows.Core.Definitions;

namespace PersistentWorkflows.Core.Registry;

internal sealed class WorkflowDefinitionRegistry : IWorkflowDefinitionRegistry
{
    private readonly Dictionary<Type, WorkflowDefinitionDescriptor> _byType = [];
    private readonly Dictionary<string, WorkflowDefinitionDescriptor> _byVersion = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private static string Key(string name, int version) => $"{name}/{version}";
    public void Register(WorkflowDefinitionDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        lock (_gate)
        {
            if (_byType.ContainsKey(descriptor.WorkflowType) || _byVersion.ContainsKey(Key(descriptor.Name, descriptor.Version)))
                throw new InvalidOperationException($"Workflow '{descriptor.Name}' version {descriptor.Version} is already registered.");
            _byType.Add(descriptor.WorkflowType, descriptor);
            _byVersion.Add(Key(descriptor.Name, descriptor.Version), descriptor);
        }
    }
    public WorkflowDefinitionDescriptor Get(Type type) => TryGet(type, out var descriptor) ? descriptor! : throw new InvalidOperationException($"Workflow '{type}' is not registered.");
    public bool TryGet(Type type, out WorkflowDefinitionDescriptor? descriptor)
    {
        ArgumentNullException.ThrowIfNull(type);
        lock (_gate) return _byType.TryGetValue(type, out descriptor);
    }
    public WorkflowDefinitionDescriptor GetByName(string name) => TryGetByName(name, out var descriptor) ? descriptor! : throw new InvalidOperationException($"Workflow '{name}' is not registered.");
    public bool TryGetByName(string name, out WorkflowDefinitionDescriptor? descriptor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        lock (_gate)
        {
            descriptor = _byVersion.Values.Where(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)).OrderByDescending(x => x.Version).FirstOrDefault();
            return descriptor is not null;
        }
    }
    public WorkflowDefinitionDescriptor GetByName(string name, int version)
    {
        lock (_gate) return _byVersion.TryGetValue(Key(name, version), out var descriptor) ? descriptor : throw new InvalidOperationException($"Workflow '{name}' version {version} is not registered.");
    }
}
