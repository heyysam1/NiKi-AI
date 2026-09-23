using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Text.Json;
using NikiAI.Core.Tools;
using NikiAI.Core.Vision;

namespace NikiAI.Tools;

/// <summary>
/// Tool for capturing the screen on-demand.
/// Risk Level 2 (Sensitive): requires explicit user authorization via ToolExecutor / PermissionEngine.
/// Zero continuous screen capture or background polling.
/// </summary>
public class CaptureScreenTool : ITool
{
    public const string ToolId = "capture_screen";

    public string Id => ToolId;
    public string Name => "Capture Screen";
    public string Description => "Captures the user's active window, monitor, or screen region on-demand with explicit authorization.";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Sensitive;
    public TimeSpan DefaultTimeout => TimeSpan.FromSeconds(10);

    public string InputSchemaJson => """
    {
      "type": "object",
      "properties": {
        "target": { 
          "type": "string", 
          "enum": ["active_window", "primary_screen", "custom_region"],
          "description": "Screen area to capture. Default is 'active_window'."
        },
        "hwnd": { 
          "type": "string", 
          "description": "Optional window handle in hex (e.g. '0x1A2B') or decimal if capturing a specific window."
        },
        "x": { "type": "integer", "description": "Left coordinate for region scope." },
        "y": { "type": "integer", "description": "Top coordinate for region scope." },
        "width": { "type": "integer", "description": "Width for region scope." },
        "height": { "type": "integer", "description": "Height for region scope." }
      }
    }
    """;

    private readonly IScreenCaptureService _screenCaptureService;

    public CaptureScreenTool(IScreenCaptureService screenCaptureService)
    {
        _screenCaptureService = screenCaptureService ?? throw new ArgumentNullException(nameof(screenCaptureService));
    }

    public async Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var (scope, hwnd, region) = ParseArguments(call.ArgumentsJson);

            ScreenCaptureResult captureResult;
            if (hwnd.HasValue && hwnd.Value != nint.Zero)
            {
                captureResult = await _screenCaptureService.CaptureWindowAsync(hwnd.Value, cancellationToken);
            }
            else
            {
                captureResult = scope switch
                {
                    ScreenTargetScope.PrimaryScreen => await _screenCaptureService.CapturePrimaryScreenAsync(cancellationToken),
                    ScreenTargetScope.CustomRegion when region.HasValue => await _screenCaptureService.CaptureRegionAsync(region.Value, cancellationToken),
                    _ => await _screenCaptureService.CaptureActiveWindowAsync(cancellationToken)
                };
            }

            stopwatch.Stop();

            if (!captureResult.Success)
            {
                return ToolResult.Failure(call.CallId, Id, captureResult.ErrorMessage ?? "Screen capture failed.", stopwatch.Elapsed);
            }

            // Raw image bytes are ephemeral and kept in memory only; metadata is returned to the agent/caller.
            var responseJson = JsonSerializer.Serialize(new
            {
                success = true,
                captured = true,
                scope = scope.ToString(),
                source = captureResult.SourceDescription,
                width = captureResult.Width,
                height = captureResult.Height,
                byte_length = captureResult.ImageBytes?.Length ?? 0,
                message = "Screen captured successfully on-demand. Raw image data is held in volatile memory and not persisted."
            });

            return ToolResult.Success(call.CallId, Id, responseJson, stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            return ToolResult.Failure(call.CallId, Id, "Screen capture was cancelled.", stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return ToolResult.Failure(call.CallId, Id, $"Screen capture error: {ex.Message}", stopwatch.Elapsed);
        }
    }

    internal static (ScreenTargetScope scope, nint? hwnd, Rectangle? region) ParseArguments(string? argumentsJson)
    {
        var scope = ScreenTargetScope.ActiveWindow;
        nint? hwnd = null;
        Rectangle? region = null;

        if (string.IsNullOrWhiteSpace(argumentsJson))
        {
            return (scope, hwnd, region);
        }

        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("target", out var targetElem))
            {
                var targetStr = targetElem.GetString()?.Trim().ToLowerInvariant();
                scope = targetStr switch
                {
                    "primary_screen" or "primary_monitor" or "all_screens" => ScreenTargetScope.PrimaryScreen,
                    "custom_region" or "region" => ScreenTargetScope.CustomRegion,
                    _ => ScreenTargetScope.ActiveWindow
                };
            }

            if (root.TryGetProperty("hwnd", out var hwndElem))
            {
                var hwndStr = hwndElem.GetString()?.Trim() ?? string.Empty;
                if (hwndStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    if (long.TryParse(hwndStr[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsedLong))
                    {
                        hwnd = (nint)parsedLong;
                    }
                }
                else if (long.TryParse(hwndStr, out var parsedDecimal))
                {
                    hwnd = (nint)parsedDecimal;
                }
            }

            if (scope == ScreenTargetScope.CustomRegion)
            {
                int x = root.TryGetProperty("x", out var xElem) && xElem.TryGetInt32(out var px) ? px : 0;
                int y = root.TryGetProperty("y", out var yElem) && yElem.TryGetInt32(out var py) ? py : 0;
                int w = root.TryGetProperty("width", out var wElem) && wElem.TryGetInt32(out var pw) ? pw : 0;
                int h = root.TryGetProperty("height", out var hElem) && hElem.TryGetInt32(out var ph) ? ph : 0;

                if (w > 0 && h > 0)
                {
                    region = new Rectangle(x, y, w, h);
                }
            }
        }
        catch { }

        return (scope, hwnd, region);
    }
}
