using System.Diagnostics;
using System.Text.Json;
using NikiAI.Core.Memory;
using NikiAI.Core.Tools;

namespace NikiAI.Tools;

/// <summary>
/// Tool for explicitly saving facts, user preferences, or project notes to persistent memory.
/// Requires explicit user intent; must never be called silently by passive agent inference.
/// </summary>
public class MemorySaveTool : ITool
{
    public const string ToolId = "memory_save";

    public string Id => ToolId;
    public string Name => "Save Memory";
    public string Description => "Explicitly saves a user fact, preference, or project note to persistent memory.";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.LowRiskReversible;
    public TimeSpan DefaultTimeout => TimeSpan.FromSeconds(10);

    public string InputSchemaJson => """
    {
      "type": "object",
      "properties": {
        "content": { "type": "string", "description": "The exact fact, preference, or note to remember." },
        "category": { "type": "string", "enum": ["LongTerm", "Project"], "description": "Target memory category (default: LongTerm)." },
        "project_id": { "type": "string", "description": "Optional project identifier if saving project-scoped memory." },
        "context_explanation": { "type": "string", "description": "Optional context or reason for saving this memory." }
      },
      "required": ["content"]
    }
    """;

    private readonly IMemoryService _memoryService;

    public MemorySaveTool(IMemoryService memoryService)
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
                return ToolResult.Failure(call.CallId, Id, "Cannot save memory: Memory is disabled by privacy settings.", stopwatch.Elapsed);
            }

            using var doc = JsonDocument.Parse(call.ArgumentsJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("content", out var contentElem) || string.IsNullOrWhiteSpace(contentElem.GetString()))
            {
                stopwatch.Stop();
                return ToolResult.Failure(call.CallId, Id, "Missing required argument 'content'.", stopwatch.Elapsed);
            }

            var content = contentElem.GetString()!.Trim();

            var category = MemoryCategory.LongTerm;
            if (root.TryGetProperty("category", out var catElem))
            {
                var catStr = catElem.GetString();
                if (!string.IsNullOrWhiteSpace(catStr) && Enum.TryParse<MemoryCategory>(catStr, true, out var parsedCat))
                {
                    category = parsedCat;
                }
            }

            string? projectId = null;
            if (root.TryGetProperty("project_id", out var projElem))
            {
                projectId = projElem.GetString()?.Trim();
            }

            string? contextExp = null;
            if (root.TryGetProperty("context_explanation", out var expElem))
            {
                contextExp = expElem.GetString()?.Trim();
            }

            var savedItem = await _memoryService.SaveExplicitMemoryAsync(
                content: content,
                category: category,
                contextExplanation: contextExp,
                projectId: projectId,
                cancellationToken: cancellationToken
            );

            stopwatch.Stop();

            var outputJson = JsonSerializer.Serialize(new
            {
                success = true,
                saved = true,
                id = savedItem.Id,
                category = savedItem.Category.ToString(),
                content = savedItem.Content,
                created_at = savedItem.CreatedAt.ToString("O"),
                message = $"Successfully saved {savedItem.Category} memory."
            });

            return ToolResult.Success(call.CallId, Id, outputJson, stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            return ToolResult.Failure(call.CallId, Id, "Memory save operation was cancelled.", stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return ToolResult.Failure(call.CallId, Id, $"Failed to save memory: {ex.Message}", stopwatch.Elapsed);
        }
    }
}
