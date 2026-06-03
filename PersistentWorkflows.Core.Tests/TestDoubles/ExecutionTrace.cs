namespace PersistentWorkflows.Core.Tests.TestDoubles;

public sealed class ExecutionTrace
{
    private readonly List<string> _entries = [];

    public IReadOnlyList<string> Entries => _entries;

    public void Add(string value) => _entries.Add(value);
}