using System.Collections.Concurrent;
using NikiAI.Core.Tools;

namespace NikiAI.Tools;

public interface IToolRegistry
{
    void RegisterTool(ITool tool);
    ITool? GetTool(string toolId);
    IReadOnlyCollection<ITool> GetAllTools();
    bool UnregisterTool(string toolId);
    int Count { get; }
}

/// <summary>
/// Central registry of available agent tools.
/// </summary>
public class ToolRegistry : IToolRegistry
{
    private readonly ConcurrentDictionary<string, ITool> _tools = new(StringComparer.OrdinalIgnoreCase);

    public int Count => _tools.Count;

    public void RegisterTool(ITool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        _tools[tool.Id] = tool;
    }

    public ITool? GetTool(string toolId)
    {
        if (string.IsNullOrWhiteSpace(toolId)) return null;
        return _tools.TryGetValue(toolId, out var tool) ? tool : null;
    }

    public IReadOnlyCollection<ITool> GetAllTools()
    {
        return _tools.Values.ToList().AsReadOnly();
    }

    public bool UnregisterTool(string toolId)
    {
        if (string.IsNullOrWhiteSpace(toolId)) return false;
        return _tools.TryRemove(toolId, out _);
    }
}
