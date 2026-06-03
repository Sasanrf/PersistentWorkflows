namespace PersistentWorkflows.Abstractions.Serialization;

public interface IWorkflowContextSerializer
{
    string Serialize<TContext>(TContext context)
        where TContext : class;

    TContext Deserialize<TContext>(string json)
        where TContext : class;
}