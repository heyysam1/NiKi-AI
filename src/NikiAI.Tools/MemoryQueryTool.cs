using System.Diagnostics;
using System.Text.Json;
using NikiAI.Core.Memory;
using NikiAI.Core.Tools;

namespace NikiAI.Tools;

/// <summary>
/// Tool for querying or searching stored memories across categories and projects.
/// </summary>
public class MemoryQueryTool : ITool
{
    public const string ToolId = "memory_query";

    public string Id => ToolId;
    public string Name => "Query Memory";
    public string Description => "Searches or lists stored memories by text query, category, or project.";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Informational;
    public TimeSpan DefaultTimeout => TimeSpan.FromSeconds(10);

    public string InputSchemaJson => """
    {
      "type": "object",
      "properties": {
        "query": { "type": "string", "description": "Optional search term to filter memories." },
        "category": { "type": "string", "enum": ["LongTerm", "Project"], "description": "Optional category filter." },
        "project_id": { "type": "string", "description": "Optional project identifier to filter project memories." }
      }
    }
    """;

    private readonly IMemoryService _memoryService;

    public MemoryQueryTool(IMemoryService memoryService)
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
                return ToolResult.Failure(call.CallId, Id, "Cannot query memory: Memory is disabled by privacy settings.", stopwatch.Elapsed);
            }

            string? query = null;
            MemoryCategory? category = null;
            string? projectId = null;

            if (!string.IsNullOrWhiteSpace(call.ArgumentsJson))
            {
                using var doc = JsonDocument.Parse(call.ArgumentsJson);
                var root = doc.RootElement;

                if (root.TryGetProperty("query", out var qElem))
                {
                    query = qElem.GetString();
                }

                if (root.TryGetProperty("category", out var catElem))
                {
                    var catStr = catElem.GetString();
                    if (!string.IsNullOrWhiteSpace(catStr) && Enum.TryParse<MemoryCategory>(catStr, true, out var parsedCat))
                    {
                        category = parsedCat;
                    }
                }

                if (root.TryGetProperty("project_id", out var projElem))
                {
                    projectId = projElem.GetString();
                }
            }

            IReadOnlyList<MemoryItem> results;
            if (!string.IsNullOrWhiteSpace(projectId))
            {
                results = await _memoryService.GetProjectMemoriesAsync(projectId, cancellationToken);
            }
            else
            {
                results = await _memoryService.SearchMemoriesAsync(query ?? string.Empty, category, cancellationToken);
            }

            stopwatch.Stop();

            var outputJson = JsonSerializer.Serialize(new
            {
                success = true,
                count = results.Count,
                memories = results.Select(m => new
                {
                    id = m.Id,
                    category = m.Category.ToString(),
                    content = m.Content,
                    context_explanation = m.ContextExplanation,
                    created_at = m.CreatedAt.ToString("O"),
                    project_id = m.ProjectId,
                    is_pinned = m.IsPinned
                })
            });

            return ToolResult.Success(call.CallId, Id, outputJson, stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            return ToolResult.Failure(call.CallId, Id, "Memory query operation was cancelled.", stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return ToolResult.Failure(call.CallId, Id, $"Failed to query memory: {ex.Message}", stopwatch.Elapsed);
        }
    }
}
