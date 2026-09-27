using NikiAI.Core.Agent;
using NikiAI.Core.Tools;

namespace NikiAI.Tools;

/// <summary>
/// Read-only catalog providing tool definitions/schemas and persistence metadata to AgentOperator.
/// Contains NO execution or authorization logic, preserving the security boundary.
/// </summary>
public class ToolCatalog : IToolCatalog
{
    private readonly IToolRegistry _toolRegistry;
    private readonly HashSet<string> _durableToolNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "run_workflow",
        "workflow_run",
        "browser_crawl",
        "system_backup"
    };

    public ToolCatalog(IToolRegistry toolRegistry)
    {
        _toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
    }

    public IReadOnlyList<ToolDefinition> GetToolDefinitions()
    {
        var tools = _toolRegistry.GetAllTools();
        var definitions = new List<ToolDefinition>(tools.Count);

        foreach (var tool in tools)
        {
            definitions.Add(new ToolDefinition(
                tool.Id,
                tool.Description,
                tool.InputSchemaJson
            ));
        }

        return definitions;
    }

    public bool RequiresDurablePersistence(string toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return false;
        }

        if (_durableToolNames.Contains(toolName))
        {
            return true;
        }

        var tool = _toolRegistry.GetTool(toolName);
        if (tool != null)
        {
            // Sensitive and HighRisk tools or long-running workflows warrant durable persistence
            if (tool.RiskLevel >= ToolRiskLevel.Sensitive)
            {
                return true;
            }
        }

        return false;
    }

    public void RegisterDurableTool(string toolName)
    {
        if (!string.IsNullOrWhiteSpace(toolName))
        {
            _durableToolNames.Add(toolName.Trim());
        }
    }
}
