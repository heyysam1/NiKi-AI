using System.Diagnostics;
using System.Text.Json;
using NikiAI.Core.Tools;

namespace NikiAI.Tools;

/// <summary>
/// Tool for searching the web.
/// Integrates with IWebSearchService; strictly prohibits Chrome and ensures Edge/Brave compatibility.
/// </summary>
public class SearchWebTool : ITool
{
    public const string ToolId = "search_web";

    public string Id => ToolId;
    public string Name => "Search Web";
    public string Description => "Performs a web search for public information and references. Uses Edge/Brave compatible services; Chrome is prohibited.";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Informational;
    public TimeSpan DefaultTimeout => TimeSpan.FromSeconds(10);

    public string InputSchemaJson => """
    {
      "type": "object",
      "required": ["query"],
      "properties": {
        "query": { "type": "string", "minLength": 1 },
        "max_results": { "type": "integer", "minimum": 1, "maximum": 10 }
      }
    }
    """;

    private readonly IWebSearchService _searchService;

    public SearchWebTool(IWebSearchService searchService)
    {
        _searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));
    }

    public async Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var doc = JsonDocument.Parse(call.ArgumentsJson);
            var root = doc.RootElement;

            var query = root.GetProperty("query").GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(query))
            {
                stopwatch.Stop();
                return ToolResult.Failure(call.CallId, Id, "Search query cannot be empty.", stopwatch.Elapsed);
            }

            // Check if query targets Google Chrome automation or installation
            if (query.Contains("install chrome", StringComparison.OrdinalIgnoreCase) ||
                query.Contains("download chrome", StringComparison.OrdinalIgnoreCase) ||
                query.Contains("automate chrome", StringComparison.OrdinalIgnoreCase))
            {
                stopwatch.Stop();
                return ToolResult.Failure(
                    call.CallId,
                    Id,
                    "Policy Violation: Google Chrome operations are prohibited. Use Microsoft Edge or Brave.",
                    stopwatch.Elapsed);
            }

            int maxResults = 5;
            if (root.TryGetProperty("max_results", out var maxElem) && maxElem.TryGetInt32(out var customMax))
            {
                maxResults = Math.Clamp(customMax, 1, 10);
            }

            var response = await _searchService.SearchAsync(query, maxResults, cancellationToken);
            stopwatch.Stop();

            var outputObj = new
            {
                query = response.Query,
                total_results = response.TotalCount,
                results = response.Results.Select(r => new
                {
                    title = r.Title,
                    snippet = r.Snippet,
                    url = r.Url
                }).ToList()
            };

            return ToolResult.Success(call.CallId, Id, JsonSerializer.Serialize(outputObj), stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            return ToolResult.Failure(call.CallId, Id, "Tool execution was cancelled.", stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return ToolResult.Failure(call.CallId, Id, $"Web search failed: {ex.Message}", stopwatch.Elapsed);
        }
    }
}
