using System.Diagnostics;
using System.Text.Json;
using NikiAI.Core.Automation;
using NikiAI.Core.Tools;

namespace NikiAI.Tools;

/// <summary>
/// Tool for listing recently active applications on the desktop in Z-order.
/// </summary>
public class RecentAppsTool : ITool
{
    public const string ToolId = "recent_apps";

    public string Id => ToolId;
    public string Name => "Get Recent Applications";
    public string Description => "Returns recently active application windows on the desktop sorted by activation order.";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.LowRiskReversible;
    public TimeSpan DefaultTimeout => TimeSpan.FromSeconds(10);

    public string InputSchemaJson => """
    {
      "type": "object",
      "properties": {
        "limit": { "type": "integer", "minimum": 1, "maximum": 20 }
      }
    }
    """;

    private readonly IWindowsAutomationService _automationService;

    public RecentAppsTool(IWindowsAutomationService automationService)
    {
        _automationService = automationService ?? throw new ArgumentNullException(nameof(automationService));
    }

    public async Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var limit = 10;
            if (!string.IsNullOrWhiteSpace(call.ArgumentsJson) && call.ArgumentsJson != "{}")
            {
                using var doc = JsonDocument.Parse(call.ArgumentsJson);
                var root = doc.RootElement;
                if (root.TryGetProperty("limit", out var limitElem) && limitElem.TryGetInt32(out var l))
                {
                    limit = Math.Clamp(l, 1, 20);
                }
            }

            var apps = await _automationService.GetRecentAppsAsync(limit, cancellationToken);

            var results = apps.Select(a => new
            {
                process_id = a.ProcessId,
                process_name = a.ProcessName,
                window_title = a.WindowTitle,
                handle = $"0x{a.MainWindowHandle:X}"
            }).ToList();

            stopwatch.Stop();
            var outputJson = JsonSerializer.Serialize(new
            {
                success = true,
                count = results.Count,
                recent_apps = results
            });

            return ToolResult.Success(call.CallId, Id, outputJson, stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            return ToolResult.Failure(call.CallId, Id, "Recent apps query was cancelled.", stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return ToolResult.Failure(call.CallId, Id, $"Failed to retrieve recent apps: {ex.Message}", stopwatch.Elapsed);
        }
    }
}
