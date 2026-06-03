using System.Text.Json;
using PersistentWorkflows.Abstractions.Serialization;

namespace PersistentWorkflows.Core.Serialization;

internal sealed class SystemTextJsonWorkflowContextSerializer : IWorkflowContextSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public string Serialize<TContext>(TContext context)
        where TContext : class
    {
        return JsonSerializer.Serialize(context, Options);
    }

    public TContext Deserialize<TContext>(string json)
        where TContext : class
    {
        return JsonSerializer.Deserialize<TContext>(json, Options)
               ?? throw new InvalidOperationException("Failed to deserialize workflow context.");
    }
}