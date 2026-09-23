using System.Diagnostics;
using System.Text.Json;
using NikiAI.Core.Automation;
using NikiAI.Core.Tools;

namespace NikiAI.Tools;

/// <summary>
/// Tool for listing active top-level desktop application windows.
/// </summary>
public class AppListTool : ITool
{
    public const string ToolId = "app_list";

    public string Id => ToolId;
    public string Name => "List Running Applications";
    public string Description => "Lists active top-level application windows currently running on the Windows desktop.";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.LowRiskReversible;
    public TimeSpan DefaultTimeout => TimeSpan.FromSeconds(10);

    public string InputSchemaJson => """
    {
      "type": "object",
      "properties": {
        "filter": { "type": "string" },
        "limit": { "type": "integer", "minimum": 1, "maximum": 50 }
      }
    }
    """;

    private readonly IWindowsAutomationService _automationService;

    public AppListTool(IWindowsAutomationService automationService)
    {
        _automationService = automationService ?? throw new ArgumentNullException(nameof(automationService));
    }

    public async Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            string? filter = null;
            var limit = 30;

            if (!string.IsNullOrWhiteSpace(call.ArgumentsJson) && call.ArgumentsJson != "{}")
            {
                using var doc = JsonDocument.Parse(call.ArgumentsJson);
                var root = doc.RootElement;

                if (root.TryGetProperty("filter", out var filterElem) && filterElem.ValueKind == JsonValueKind.String)
                {
                    filter = filterElem.GetString()?.Trim();
                }

                if (root.TryGetProperty("limit", out var limitElem) && limitElem.TryGetInt32(out var l))
                {
                    limit = Math.Clamp(l, 1, 50);
                }
            }

            var apps = await _automationService.GetRunningAppsAsync(cancellationToken);

            if (!string.IsNullOrWhiteSpace(filter))
            {
                apps = apps.Where(a =>
                    a.ProcessName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    a.WindowTitle.Contains(filter, StringComparison.OrdinalIgnoreCase)
                ).ToList();
            }

            var results = apps.Take(limit).Select(a => new
            {
                process_id = a.ProcessId,
                process_name = a.ProcessName,
                window_title = a.WindowTitle,
                handle = $"0x{a.MainWindowHandle:X}",
                is_responding = a.IsResponding
            }).ToList();

            stopwatch.Stop();
            var outputJson = JsonSerializer.Serialize(new
            {
                success = true,
                count = results.Count,
                apps = results
            });

            return ToolResult.Success(call.CallId, Id, outputJson, stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            return ToolResult.Failure(call.CallId, Id, "App list query was cancelled.", stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return ToolResult.Failure(call.CallId, Id, $"Failed to retrieve running apps: {ex.Message}", stopwatch.Elapsed);
        }
    }
}
