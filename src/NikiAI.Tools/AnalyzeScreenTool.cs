using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Text.Json;
using NikiAI.Core.Tools;
using NikiAI.Core.Vision;

namespace NikiAI.Tools;

/// <summary>
/// Tool for capturing and analyzing the screen using an on-demand vision provider.
/// Risk Level 2 (Sensitive): requires explicit user authorization via ToolExecutor / PermissionEngine.
/// Zero continuous screen capture or background polling.
/// Untrusted data boundary: screen text and observations are quarantined inside untrusted blocks.
/// Screen bytes and VisionAnalysisResult are ephemeral and not persisted.
/// </summary>
public class AnalyzeScreenTool : ITool
{
    public const string ToolId = "analyze_screen";

    public string Id => ToolId;
    public string Name => "Analyze Screen";
    public string Description => "Captures the user's screen on-demand and analyzes it using a vision model with explicit user authorization.";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Sensitive;
    public TimeSpan DefaultTimeout => TimeSpan.FromSeconds(30);

    public string InputSchemaJson => """
    {
      "type": "object",
      "required": ["prompt"],
      "properties": {
        "prompt": {
          "type": "string",
          "description": "User question or instruction describing what to analyze on the screen."
        },
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
    private readonly IVisionProvider _visionProvider;

    public AnalyzeScreenTool(IScreenCaptureService screenCaptureService, IVisionProvider visionProvider)
    {
        _screenCaptureService = screenCaptureService ?? throw new ArgumentNullException(nameof(screenCaptureService));
        _visionProvider = visionProvider ?? throw new ArgumentNullException(nameof(visionProvider));
    }

    public async Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var (prompt, scope, hwnd, region) = ParseArguments(call.ArgumentsJson);

            if (string.IsNullOrWhiteSpace(prompt))
            {
                stopwatch.Stop();
                return ToolResult.Failure(call.CallId, Id, "A prompt describing what to analyze on screen is required.", stopwatch.Elapsed);
            }

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

            if (!captureResult.Success)
            {
                stopwatch.Stop();
                return ToolResult.Failure(call.CallId, Id, captureResult.ErrorMessage ?? "Screen capture failed.", stopwatch.Elapsed);
            }

            if (captureResult.ImageBytes == null || captureResult.ImageBytes.Length == 0)
            {
                stopwatch.Stop();
                return ToolResult.Failure(call.CallId, Id, "Screen capture returned empty image data.", stopwatch.Elapsed);
            }

            var visionRequest = new VisionAnalysisRequest(
                ImageBytes: captureResult.ImageBytes,
                Prompt: prompt,
                TargetDescription: captureResult.SourceDescription
            );

            var visionResult = await _visionProvider.AnalyzeImageAsync(visionRequest, cancellationToken);
            stopwatch.Stop();

            if (!visionResult.Success)
            {
                return ToolResult.Failure(call.CallId, Id, visionResult.ErrorMessage ?? "Vision analysis failed.", stopwatch.Elapsed);
            }

            // Untrusted data boundary quarantine:
            // Screen content and OCR/vision observations must be explicitly wrapped in an untrusted block
            // so they cannot hijack system prompts, instructions, or bypass permission gates.
            var sb = new StringBuilder();
            sb.AppendLine("=== UNTRUSTED SCREEN CONTENT START ===");
            sb.AppendLine($"Summary: {visionResult.Summary}");

            if (!string.IsNullOrWhiteSpace(visionResult.ExtractedText))
            {
                sb.AppendLine($"Extracted Text: {visionResult.ExtractedText}");
            }

            if (visionResult.DetectedElements != null && visionResult.DetectedElements.Count > 0)
            {
                sb.AppendLine("Detected Elements:");
                foreach (var elem in visionResult.DetectedElements)
                {
                    sb.AppendLine($"- {elem}");
                }
            }
            sb.Append("=== UNTRUSTED SCREEN CONTENT END ===");

            return ToolResult.Success(call.CallId, Id, sb.ToString(), stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            return ToolResult.Failure(call.CallId, Id, "Screen analysis was cancelled.", stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return ToolResult.Failure(call.CallId, Id, $"Screen analysis error: {ex.Message}", stopwatch.Elapsed);
        }
    }

    internal static (string prompt, ScreenTargetScope scope, nint? hwnd, Rectangle? region) ParseArguments(string? argumentsJson)
    {
        var prompt = string.Empty;
        var scope = ScreenTargetScope.ActiveWindow;
        nint? hwnd = null;
        Rectangle? region = null;

        if (string.IsNullOrWhiteSpace(argumentsJson))
        {
            return (prompt, scope, hwnd, region);
        }

        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("prompt", out var promptElem))
            {
                prompt = promptElem.GetString()?.Trim() ?? string.Empty;
            }

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

        return (prompt, scope, hwnd, region);
    }
}
