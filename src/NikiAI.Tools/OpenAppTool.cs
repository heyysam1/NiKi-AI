using System.Diagnostics;
using System.Text.Json;
using NikiAI.Core.Tools;

namespace NikiAI.Tools;

/// <summary>
/// Tool for launching controlled, approved applications.
/// Resolves through IApprovedAppRegistry; strictly prohibits Chrome and arbitrary executable paths.
/// </summary>
public class OpenAppTool : ITool
{
    public const string ToolId = "open_app";

    public string Id => ToolId;
    public string Name => "Open Application";
    public string Description => "Opens an approved application from the controlled application catalog.";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.LowRiskReversible;
    public TimeSpan DefaultTimeout => TimeSpan.FromSeconds(10);

    public string InputSchemaJson => """
    {
      "type": "object",
      "required": ["app_name"],
      "properties": {
        "app_name": { "type": "string", "minLength": 1 },
        "arguments": { "type": "string" }
      }
    }
    """;

    private readonly IApprovedAppRegistry _appRegistry;
    private readonly IProcessLauncher _processLauncher;

    public OpenAppTool(IApprovedAppRegistry appRegistry, IProcessLauncher processLauncher)
    {
        _appRegistry = appRegistry ?? throw new ArgumentNullException(nameof(appRegistry));
        _processLauncher = processLauncher ?? throw new ArgumentNullException(nameof(processLauncher));
    }

    public async Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var doc = JsonDocument.Parse(call.ArgumentsJson);
            var root = doc.RootElement;
            var appName = root.GetProperty("app_name").GetString()?.Trim() ?? string.Empty;

            string? arguments = null;
            if (root.TryGetProperty("arguments", out var argElem) && argElem.ValueKind == JsonValueKind.String)
            {
                arguments = argElem.GetString()?.Trim();
            }

            // Chrome prohibition check
            if (_appRegistry.IsProhibitedApp(appName, out var prohibitionReason))
            {
                stopwatch.Stop();
                return ToolResult.Failure(call.CallId, Id, prohibitionReason!, stopwatch.Elapsed);
            }

            // Approved app resolution
            if (!_appRegistry.TryResolveApp(appName, out var appEntry) || appEntry == null)
            {
                stopwatch.Stop();
                return ToolResult.Failure(
                    call.CallId,
                    Id,
                    $"Application '{appName}' is not in the controlled approved application catalog. Arbitrary executable execution is prohibited.",
                    stopwatch.Elapsed);
            }

            var launchResult = await _processLauncher.LaunchApprovedAppAsync(appEntry, arguments, cancellationToken);
            stopwatch.Stop();

            if (!launchResult.Success)
            {
                return ToolResult.Failure(
                    call.CallId,
                    Id,
                    $"Failed to launch application '{appEntry.DisplayName}': {launchResult.ErrorMessage}",
                    stopwatch.Elapsed);
            }

            var outputObj = new
            {
                app_name = appEntry.DisplayName,
                key = appEntry.Key,
                process_id = launchResult.ProcessId,
                status = "Launched"
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
            return ToolResult.Failure(call.CallId, Id, $"Error launching application: {ex.Message}", stopwatch.Elapsed);
        }
    }
}
