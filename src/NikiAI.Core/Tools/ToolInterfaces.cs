namespace NikiAI.Core.Tools;

/// <summary>
/// Service abstraction for interacting with the clipboard.
/// Local only: clipboard data is never automatically transmitted to remote AI providers.
/// </summary>
public interface IClipboardService
{
    Task<string?> GetTextAsync(CancellationToken cancellationToken = default);
    Task SetTextAsync(string text, CancellationToken cancellationToken = default);
}

/// <summary>
/// Approved application catalog entry for controlled application launching.
/// Eliminates arbitrary executable execution and shell injection risks.
/// </summary>
public record ApprovedAppEntry(
    string Key,
    string DisplayName,
    string ExecutableName,
    string Description
);

/// <summary>
/// Registry governing approved applications allowed to be launched by open_app.
/// Strictly prohibits Google Chrome and rejects arbitrary paths.
/// </summary>
public interface IApprovedAppRegistry
{
    bool TryResolveApp(string appNameOrKey, out ApprovedAppEntry? entry);
    IReadOnlyList<ApprovedAppEntry> GetApprovedApps();
    bool IsProhibitedApp(string appNameOrKey, out string? reason);
}

/// <summary>
/// Result of launching an approved application process.
/// </summary>
public record ProcessLaunchResult(
    bool Success,
    int? ProcessId,
    string AppName,
    string? ErrorMessage
)
{
    public static ProcessLaunchResult Successful(string appName, int? processId = null) =>
        new(true, processId, appName, null);

    public static ProcessLaunchResult Failed(string appName, string errorMessage) =>
        new(false, null, appName, errorMessage);
}

/// <summary>
/// Service abstraction for safely launching controlled, approved applications.
/// </summary>
public interface IProcessLauncher
{
    Task<ProcessLaunchResult> LaunchApprovedAppAsync(
        ApprovedAppEntry app,
        string? arguments = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Individual web search result item.
/// </summary>
public record WebSearchResultItem(
    string Title,
    string Snippet,
    string Url
);

/// <summary>
/// Response payload for web search queries.
/// </summary>
public record WebSearchResponse(
    string Query,
    IReadOnlyList<WebSearchResultItem> Results,
    int TotalCount
);

/// <summary>
/// Service abstraction for executing web search queries.
/// Compatible with Edge/Brave; strictly prohibits Google Chrome.
/// </summary>
public interface IWebSearchService
{
    Task<WebSearchResponse> SearchAsync(
        string query,
        int maxResults = 5,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Exception thrown when a tool call fails input schema validation.
/// </summary>
public class ToolValidationException : Exception
{
    public string ToolId { get; }
    public string ValidationError { get; }

    public ToolValidationException(string toolId, string validationError)
        : base($"Validation failed for tool '{toolId}': {validationError}")
    {
        ToolId = toolId;
        ValidationError = validationError;
    }
}

/// <summary>
/// Structured audit record of a tool execution.
/// Supports both task-associated execution (non-null TaskId) and standalone execution (null TaskId).
/// Sensitive arguments and outputs are redacted prior to logging.
/// </summary>
public record ToolAuditRecord(
    string? TaskId,
    string CallId,
    string ToolId,
    string SanitizedArgumentsJson,
    bool IsSuccess,
    string? SanitizedOutputJson,
    string? ErrorMessage,
    TimeSpan Duration,
    DateTimeOffset Timestamp,
    ToolRiskLevel RiskLevel,
    bool RequiresApproval
);

/// <summary>
/// Audit logger contract for capturing tool execution events in all contexts.
/// </summary>
public interface IToolAuditLogger
{
    Task LogExecutionAsync(ToolAuditRecord record, CancellationToken cancellationToken = default);
    IReadOnlyList<ToolAuditRecord> GetRecentRecords(int limit = 50);
}
