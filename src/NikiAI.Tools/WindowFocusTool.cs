using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using NikiAI.Core.Automation;
using NikiAI.Core.Tools;

namespace NikiAI.Tools;

/// <summary>
/// Tool for bringing a desktop application window to the foreground.
/// </summary>
public class WindowFocusTool : ITool
{
    public const string ToolId = "window_focus";

    public string Id => ToolId;
    public string Name => "Focus Window";
    public string Description => "Brings an application window to the foreground by window handle or application name.";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.LowRiskReversible;
    public TimeSpan DefaultTimeout => TimeSpan.FromSeconds(10);

    public string InputSchemaJson => """
    {
      "type": "object",
      "properties": {
        "hwnd": { "type": "string" },
        "app_name": { "type": "string" }
      }
    }
    """;

    private readonly IWindowsAutomationService _automationService;

    public WindowFocusTool(IWindowsAutomationService automationService)
    {
        _automationService = automationService ?? throw new ArgumentNullException(nameof(automationService));
    }

    public async Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var doc = JsonDocument.Parse(call.ArgumentsJson);
            var root = doc.RootElement;

            nint targetHwnd = nint.Zero;

            if (root.TryGetProperty("hwnd", out var hwndElem))
            {
                var hwndStr = hwndElem.GetString()?.Trim() ?? string.Empty;
                if (hwndStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    if (long.TryParse(hwndStr[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsedLong))
                    {
                        targetHwnd = (nint)parsedLong;
                    }
                }
                else if (long.TryParse(hwndStr, out var parsedDecimal))
                {
                    targetHwnd = (nint)parsedDecimal;
                }
            }

            if (targetHwnd == nint.Zero && root.TryGetProperty("app_name", out var appElem))
            {
                var appName = appElem.GetString()?.Trim() ?? string.Empty;
                if (!string.IsNullOrEmpty(appName))
                {
                    var runningApps = await _automationService.GetRunningAppsAsync(cancellationToken);
                    var matchedApp = runningApps.FirstOrDefault(a =>
                        a.ProcessName.Equals(appName, StringComparison.OrdinalIgnoreCase) ||
                        a.WindowTitle.Contains(appName, StringComparison.OrdinalIgnoreCase)
                    );

                    if (matchedApp != null)
                    {
                        targetHwnd = matchedApp.MainWindowHandle;
                    }
                }
            }

            if (targetHwnd == nint.Zero)
            {
                stopwatch.Stop();
                return ToolResult.Failure(call.CallId, Id, "Could not determine a valid window handle to focus.", stopwatch.Elapsed);
            }

            var focused = await _automationService.FocusWindowAsync(targetHwnd, cancellationToken);
            stopwatch.Stop();

            if (focused)
            {
                var outputJson = JsonSerializer.Serialize(new
                {
                    success = true,
                    handle = $"0x{targetHwnd:X}",
                    message = "Window brought to foreground successfully."
                });

                return ToolResult.Success(call.CallId, Id, outputJson, stopwatch.Elapsed);
            }
            else
            {
                return ToolResult.Failure(call.CallId, Id, $"Failed to focus window handle 0x{targetHwnd:X}.", stopwatch.Elapsed);
            }
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            return ToolResult.Failure(call.CallId, Id, "Window focus was cancelled.", stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return ToolResult.Failure(call.CallId, Id, $"Window focus error: {ex.Message}", stopwatch.Elapsed);
        }
    }
}
