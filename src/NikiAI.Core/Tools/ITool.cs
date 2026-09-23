namespace NikiAI.Core.Tools;

/// <summary>
/// Tool risk classification levels as defined in 05_SECURITY_AND_PERMISSIONS.md.
/// </summary>
public enum ToolRiskLevel
{
    /// <summary>
    /// Level 0: No meaningful external side effect. Explanations, summaries, drafts.
    /// Auto-allow by default.
    /// </summary>
    Informational = 0,

    /// <summary>
    /// Level 1: Minor system changes that can normally be undone. Reminders, benign app launch, notes.
    /// Auto-allow if user preferences allow.
    /// </summary>
    LowRiskReversible = 1,

    /// <summary>
    /// Level 2: Meaningful external or data impact. Writing/moving files, sending messages, uploads.
    /// Default: Ask for approval unless explicitly trusted.
    /// </summary>
    Sensitive = 2,

    /// <summary>
    /// Level 3: Potentially destructive or security-sensitive. Deleting files, shell execution, install software.
    /// Default: Always ask approval. Never auto-allow.
    /// </summary>
    HighRisk = 3
}

/// <summary>
/// Represents an invocation call to an agent tool.
/// </summary>
public record ToolCall(
    string CallId,
    string ToolId,
    string ArgumentsJson,
    DateTimeOffset Timestamp
);

/// <summary>
/// Represents the result of a tool execution.
/// </summary>
public record ToolResult(
    string CallId,
    string ToolId,
    bool IsSuccess,
    string? OutputJson,
    string? ErrorMessage,
    TimeSpan Duration
)
{
    public static ToolResult Success(string callId, string toolId, string outputJson, TimeSpan duration) =>
        new(callId, toolId, true, outputJson, null, duration);

    public static ToolResult Failure(string callId, string toolId, string errorMessage, TimeSpan duration) =>
        new(callId, toolId, false, null, errorMessage, duration);
}

/// <summary>
/// Tool contract interface implemented by all controlled tools in Niki AI.
/// </summary>
public interface ITool
{
    string Id { get; }
    string Name { get; }
    string Description { get; }
    ToolRiskLevel RiskLevel { get; }
    string InputSchemaJson { get; }
    TimeSpan DefaultTimeout { get; }

    Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken = default);
}
