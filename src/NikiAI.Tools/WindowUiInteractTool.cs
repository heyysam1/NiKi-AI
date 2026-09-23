using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using NikiAI.Core.Automation;
using NikiAI.Core.Tools;

namespace NikiAI.Tools;

/// <summary>
/// Tool for inspecting and invoking UI Automation elements within a window.
/// </summary>
public class WindowUiInteractTool : ITool
{
    public const string ToolId = "window_ui_interact";

    public string Id => ToolId;
    public string Name => "Window UI Interact";
    public string Description => "Inspects or interacts with UI Automation elements in a target application window.";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Sensitive;
    public TimeSpan DefaultTimeout => TimeSpan.FromSeconds(15);

    public string InputSchemaJson => """
    {
      "type": "object",
      "required": ["action"],
      "properties": {
        "hwnd": { "type": "string" },
        "app_name": { "type": "string" },
        "action": { "type": "string", "enum": ["inspect", "invoke"] },
        "element_id_or_name": { "type": "string" },
        "max_depth": { "type": "integer", "minimum": 1, "maximum": 4 }
      }
    }
    """;

    private readonly IWindowsAutomationService _automationService;

    public WindowUiInteractTool(IWindowsAutomationService automationService)
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

            var action = root.GetProperty("action").GetString()?.Trim().ToLowerInvariant() ?? "inspect";
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
                return ToolResult.Failure(call.CallId, Id, "Target window handle (hwnd) or application name (app_name) is required.", stopwatch.Elapsed);
            }

            if (action == "inspect")
            {
                var maxDepth = 2;
                if (root.TryGetProperty("max_depth", out var depthElem) && depthElem.TryGetInt32(out var d))
                {
                    maxDepth = Math.Clamp(d, 1, 4);
                }

                var elements = await _automationService.GetWindowElementsAsync(targetHwnd, maxDepth, cancellationToken);
                stopwatch.Stop();

                var outputJson = JsonSerializer.Serialize(new
                {
                    success = true,
                    handle = $"0x{targetHwnd:X}",
                    element_count = elements.Count,
                    elements = elements.Select(e => new
                    {
                        automation_id = e.AutomationId,
                        name = e.Name,
                        control_type = e.ControlType,
                        class_name = e.ClassName,
                        is_enabled = e.IsEnabled,
                        is_offscreen = e.IsOffscreen
                    })
                });

                return ToolResult.Success(call.CallId, Id, outputJson, stopwatch.Elapsed);
            }
            else if (action == "invoke")
            {
                var targetIdOrName = root.TryGetProperty("element_id_or_name", out var targetElem)
                    ? targetElem.GetString()?.Trim()
                    : null;

                if (string.IsNullOrWhiteSpace(targetIdOrName))
                {
                    stopwatch.Stop();
                    return ToolResult.Failure(call.CallId, Id, "Action 'invoke' requires 'element_id_or_name'.", stopwatch.Elapsed);
                }

                var invoked = await _automationService.InvokeElementAsync(targetHwnd, targetIdOrName, cancellationToken);
                stopwatch.Stop();

                if (invoked)
                {
                    var outputJson = JsonSerializer.Serialize(new
                    {
                        success = true,
                        handle = $"0x{targetHwnd:X}",
                        target = targetIdOrName,
                        message = "UI element invoked successfully."
                    });

                    return ToolResult.Success(call.CallId, Id, outputJson, stopwatch.Elapsed);
                }
                else
                {
                    return ToolResult.Failure(call.CallId, Id, $"Could not find or invoke UI element '{targetIdOrName}' in window 0x{targetHwnd:X}.", stopwatch.Elapsed);
                }
            }
            else
            {
                stopwatch.Stop();
                return ToolResult.Failure(call.CallId, Id, $"Unsupported action '{action}'. Supported actions are 'inspect' and 'invoke'.", stopwatch.Elapsed);
            }
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            return ToolResult.Failure(call.CallId, Id, "UI interaction was cancelled.", stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return ToolResult.Failure(call.CallId, Id, $"UI interaction failed: {ex.Message}", stopwatch.Elapsed);
        }
    }
}
