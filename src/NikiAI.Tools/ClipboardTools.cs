using System.Diagnostics;
using System.Text.Json;
using NikiAI.Core.Tools;

namespace NikiAI.Tools;

/// <summary>
/// Tool for reading text from the Windows clipboard.
/// Strictly local: content is returned solely to the local tool execution pipeline
/// and is never automatically transmitted to remote AI endpoints.
/// </summary>
public class ReadClipboardTool : ITool
{
    public const string ToolId = "read_clipboard";

    public string Id => ToolId;
    public string Name => "Read Clipboard";
    public string Description => "Reads text content from the local Windows clipboard. Local operation only; data is never automatically sent to remote AI providers.";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Informational;
    public TimeSpan DefaultTimeout => TimeSpan.FromSeconds(5);

    public string InputSchemaJson => """
    {
      "type": "object"
    }
    """;

    private readonly IClipboardService _clipboardService;

    public ReadClipboardTool(IClipboardService clipboardService)
    {
        _clipboardService = clipboardService ?? throw new ArgumentNullException(nameof(clipboardService));
    }

    public async Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var text = await _clipboardService.GetTextAsync(cancellationToken);
            stopwatch.Stop();

            var outputObj = new
            {
                has_text = !string.IsNullOrEmpty(text),
                character_count = text?.Length ?? 0,
                text = text
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
            return ToolResult.Failure(call.CallId, Id, $"Failed to read clipboard: {ex.Message}", stopwatch.Elapsed);
        }
    }
}

/// <summary>
/// Tool for copying text to the Windows clipboard.
/// </summary>
public class WriteClipboardTool : ITool
{
    public const string ToolId = "write_clipboard";

    public string Id => ToolId;
    public string Name => "Write Clipboard";
    public string Description => "Copies text to the local Windows clipboard.";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.LowRiskReversible;
    public TimeSpan DefaultTimeout => TimeSpan.FromSeconds(5);

    public string InputSchemaJson => """
    {
      "type": "object",
      "required": ["text"],
      "properties": {
        "text": { "type": "string" }
      }
    }
    """;

    private readonly IClipboardService _clipboardService;

    public WriteClipboardTool(IClipboardService clipboardService)
    {
        _clipboardService = clipboardService ?? throw new ArgumentNullException(nameof(clipboardService));
    }

    public async Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var doc = JsonDocument.Parse(call.ArgumentsJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("text", out var textElem) || textElem.ValueKind != JsonValueKind.String)
            {
                stopwatch.Stop();
                return ToolResult.Failure(call.CallId, Id, "Property 'text' must be a string.", stopwatch.Elapsed);
            }

            var text = textElem.GetString() ?? string.Empty;
            await _clipboardService.SetTextAsync(text, cancellationToken);
            stopwatch.Stop();

            var outputObj = new
            {
                success = true,
                character_count = text.Length
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
            return ToolResult.Failure(call.CallId, Id, $"Failed to write to clipboard: {ex.Message}", stopwatch.Elapsed);
        }
    }
}
