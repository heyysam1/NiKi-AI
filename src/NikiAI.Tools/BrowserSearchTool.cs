using System.Diagnostics;
using System.Text.Json;
using NikiAI.Core.Browser;
using NikiAI.Core.Tools;

namespace NikiAI.Tools;

/// <summary>
/// Agent tool for searching the web via browser automation.
/// Output is marked as untrusted external data.
/// </summary>
public class BrowserSearchTool : ITool
{
    public const string ToolId = "browser_search";

    public string Id => ToolId;
    public string Name => "Browser Search";
    public string Description => "Performs a web search using browser automation. Returns structured search results.";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Informational;
    public TimeSpan DefaultTimeout => TimeSpan.FromSeconds(25);

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

    private readonly IBrowserAutomationEngine _browserEngine;

    public BrowserSearchTool(IBrowserAutomationEngine browserEngine)
    {
        _browserEngine = browserEngine ?? throw new ArgumentNullException(nameof(browserEngine));
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

            int maxResults = 5;
            if (root.TryGetProperty("max_results", out var maxElem) && maxElem.TryGetInt32(out var customMax))
            {
                maxResults = Math.Clamp(customMax, 1, 10);
            }

            var searchOutput = await _browserEngine.SearchAsync(query, maxResults, cancellationToken);
            stopwatch.Stop();

            var outputObj = new
            {
                query = searchOutput.Query,
                provider = searchOutput.Provider,
                total_results = searchOutput.TotalCount,
                is_untrusted_external_data = searchOutput.IsUntrustedExternalData,
                results = searchOutput.Results.Select(r => new
                {
                    title = r.Title,
                    url = r.Url,
                    snippet = r.Snippet
                }).ToList()
            };

            return ToolResult.Success(call.CallId, Id, JsonSerializer.Serialize(outputObj), stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            return ToolResult.Failure(call.CallId, Id, "Search was cancelled.", stopwatch.Elapsed);
        }
        catch (BrowserUnavailableException ex)
        {
            stopwatch.Stop();
            return ToolResult.Failure(call.CallId, Id, ex.Message, stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return ToolResult.Failure(call.CallId, Id, $"Browser search failed: {ex.Message}", stopwatch.Elapsed);
        }
    }
}
