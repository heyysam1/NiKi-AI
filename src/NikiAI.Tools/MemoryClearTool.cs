using System.Diagnostics;
using System.Text.Json;
using NikiAI.Core.Memory;
using NikiAI.Core.Tools;

namespace NikiAI.Tools;

/// <summary>
/// Tool for clearing memories by category or project.
/// Classified as Sensitive risk level; triggers explicit user approval prompt.
/// </summary>
public class MemoryClearTool : ITool
{
    public const string ToolId = "memory_clear";

    public string Id => ToolId;
    public string Name => "Clear Memory";
    public string Description => "Clears memories within a specific category. Sensitive operation requiring explicit authorization.";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Sensitive;
    public TimeSpan DefaultTimeout => TimeSpan.FromSeconds(15);

    public string InputSchemaJson => """
    {
      "type": "object",
      "properties": {
        "category": { "type": "string", "enum": ["LongTerm", "Project"], "description": "The target memory category to clear." }
      },
      "required": ["category"]
    }
    """;

    private readonly IMemoryService _memoryService;

    public MemoryClearTool(IMemoryService memoryService)
    {
        _memoryService = memoryService ?? throw new ArgumentNullException(nameof(memoryService));
    }

    public async Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (!_memoryService.IsMemoryEnabled())
            {
                stopwatch.Stop();
                return ToolResult.Failure(call.CallId, Id, "Cannot clear memory: Memory is disabled by privacy settings.", stopwatch.Elapsed);
            }

            if (string.IsNullOrWhiteSpace(call.ArgumentsJson))
            {
                stopwatch.Stop();
                return ToolResult.Failure(call.CallId, Id, "Missing arguments JSON.", stopwatch.Elapsed);
            }

            using var doc = JsonDocument.Parse(call.ArgumentsJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("category", out var catElem) || string.IsNullOrWhiteSpace(catElem.GetString()))
            {
                stopwatch.Stop();
                return ToolResult.Failure(call.CallId, Id, "Missing required argument 'category'.", stopwatch.Elapsed);
            }

            var catString = catElem.GetString()!;
            if (!Enum.TryParse<MemoryCategory>(catString, true, out var category))
            {
                stopwatch.Stop();
                return ToolResult.Failure(call.CallId, Id, $"Invalid category '{catString}'. Allowed: LongTerm, Project.", stopwatch.Elapsed);
            }

            var itemsBefore = await _memoryService.GetMemoriesAsync(category, cancellationToken);
            await _memoryService.ClearCategoryAsync(category, cancellationToken);
            stopwatch.Stop();

            var responseJson = JsonSerializer.Serialize(new
            {
                cleared = true,
                category = category.ToString(),
                items_cleared = itemsBefore.Count
            });

            return ToolResult.Success(call.CallId, Id, responseJson, stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return ToolResult.Failure(call.CallId, Id, $"Failed to clear memory category: {ex.Message}", stopwatch.Elapsed);
        }
    }
}
