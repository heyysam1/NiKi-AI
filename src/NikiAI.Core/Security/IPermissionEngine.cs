using NikiAI.Core.Tools;

namespace NikiAI.Core.Security;

/// <summary>
/// User decisions for sensitive tool execution approvals.
/// </summary>
public enum ApprovalDecision
{
    AllowOnce,
    AlwaysAllow,
    Deny
}

/// <summary>
/// Terminal outcome of an approval request.
/// </summary>
public enum ApprovalOutcome
{
    Approved,
    Denied,
    TimedOut,
    Cancelled
}

/// <summary>
/// Helper to construct narrow, least-privilege permission identity keys.
/// Formats as '{toolId}:{action}:{resource}' to prevent broad tool-wide authorization.
/// </summary>
public static class PermissionScopeKey
{
    public static string Build(string toolId, string? action = null, string? resource = null)
    {
        var tool = string.IsNullOrWhiteSpace(toolId) ? "unknown_tool" : toolId.Trim().ToLowerInvariant();
        var act = string.IsNullOrWhiteSpace(action) ? "*" : action.Trim().ToLowerInvariant();
        var res = string.IsNullOrWhiteSpace(resource) ? "*" : resource.Trim().ToLowerInvariant();
        return $"{tool}:{act}:{res}";
    }
}

/// <summary>
/// Represents a structured permission request presented to the user.
/// Must state what will happen, affected resource, reversibility, and timeout expiration.
/// </summary>
public record ApprovalRequest(
    string RequestId,
    string? TaskId,
    string ToolId,
    string ScopeKey,
    ToolRiskLevel RiskLevel,
    string ActionDescription,
    string AffectedResource,
    bool IsReversible,
    string? ExternalDataDisclosureExplanation,
    string? SanitizedArguments,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    TimeSpan Timeout
);

/// <summary>
/// Structured result of an approval decision.
/// </summary>
public record ApprovalDecisionResult(
    ApprovalDecision? Decision,
    ApprovalOutcome Outcome,
    string? Reason,
    DateTimeOffset RespondedAt
)
{
    public static ApprovalDecisionResult Approved(ApprovalDecision decision, string? reason = null) =>
        new(decision, ApprovalOutcome.Approved, reason, DateTimeOffset.UtcNow);

    public static ApprovalDecisionResult Denied(string? reason = null) =>
        new(ApprovalDecision.Deny, ApprovalOutcome.Denied, reason ?? "User denied permission.", DateTimeOffset.UtcNow);

    public static ApprovalDecisionResult TimedOut(string? reason = null) =>
        new(null, ApprovalOutcome.TimedOut, reason ?? "Approval request timed out.", DateTimeOffset.UtcNow);

    public static ApprovalDecisionResult Cancelled(string? reason = null) =>
        new(null, ApprovalOutcome.Cancelled, reason ?? "Approval request was cancelled.", DateTimeOffset.UtcNow);
}

/// <summary>
/// Result of evaluating a tool execution against security policies.
/// </summary>
public record PermissionEvaluationResult(
    bool IsAllowed,
    bool RequiresUserPrompt,
    ApprovalRequest? PromptRequest,
    string Reason
)
{
    public static PermissionEvaluationResult Allowed(string reason) =>
        new(true, false, null, reason);

    public static PermissionEvaluationResult Denied(string reason) =>
        new(false, false, null, reason);

    public static PermissionEvaluationResult NeedsPrompt(ApprovalRequest request, string reason) =>
        new(false, true, request, reason);
}

/// <summary>
/// Contract for displaying approval requests and collecting user decisions.
/// </summary>
public interface IApprovalPromptHandler
{
    Task<ApprovalDecisionResult> RequestApprovalAsync(
        ApprovalRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Structured audit record for permission evaluations and approval decisions.
/// Sensitive details and secrets are redacted prior to logging.
/// </summary>
public record PermissionAuditRecord(
    string RequestId,
    string? TaskId,
    string ToolId,
    string ScopeKey,
    ToolRiskLevel RiskLevel,
    ApprovalOutcome Outcome,
    ApprovalDecision? Decision,
    string Actor,
    DateTimeOffset Timestamp,
    TimeSpan Duration,
    string? SanitizedDetails
);

/// <summary>
/// Contract for recording permission audit events.
/// </summary>
public interface IPermissionAuditLogger
{
    Task LogPermissionEventAsync(PermissionAuditRecord record, CancellationToken cancellationToken = default);
    IReadOnlyList<PermissionAuditRecord> GetRecentAuditRecords(int limit = 50);
}

/// <summary>
/// Storage contract for persistent permission rules.
/// </summary>
public interface IPermissionRuleRepository
{
    Task<IReadOnlyDictionary<string, ApprovalDecision>> LoadRulesAsync(CancellationToken cancellationToken = default);
    Task SaveRuleAsync(string scopeKey, string toolId, ApprovalDecision decision, ToolRiskLevel riskLevel, CancellationToken cancellationToken = default);
    Task<bool> DeleteRuleAsync(string scopeKey, CancellationToken cancellationToken = default);
    Task ClearAllRulesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Permission engine interface governing all tool executions.
/// Serves as the non-bypassable security boundary between planner and execution.
/// </summary>
public interface IPermissionEngine
{
    Task<PermissionEvaluationResult> EvaluateToolExecutionAsync(
        string taskId,
        ITool tool,
        ToolCall call,
        CancellationToken cancellationToken = default);

    Task<ApprovalDecisionResult> RequestApprovalAsync(
        ApprovalRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> RecordDecisionAsync(
        string scopeKey,
        string toolId,
        ApprovalDecision decision,
        ToolRiskLevel riskLevel,
        CancellationToken cancellationToken = default);

    void RecordDecision(string toolId, ApprovalDecision decision);

    Task<bool> RevokeDecisionAsync(string scopeKey, CancellationToken cancellationToken = default);
    IReadOnlyDictionary<string, ApprovalDecision> GetPersistentDecisions();
    Task ReloadRulesAsync(CancellationToken cancellationToken = default);
    void SetPromptHandler(IApprovalPromptHandler promptHandler);
}
