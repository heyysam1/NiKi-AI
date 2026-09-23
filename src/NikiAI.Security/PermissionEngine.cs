using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NikiAI.Core.Logging;
using NikiAI.Core.Security;
using NikiAI.Core.Tasks;
using NikiAI.Core.Tools;

namespace NikiAI.Security;

/// <summary>
/// Security permission engine enforcing least privilege, explicit approvals, and safe defaults.
/// Serves as the core security boundary between planner intents and actual tool execution.
/// </summary>
public class PermissionEngine : IPermissionEngine
{
    private readonly IPermissionRuleRepository? _ruleRepository;
    private readonly IPermissionAuditLogger? _auditLogger;
    private readonly ITaskRepository? _taskRepository;
    private readonly ILogger<PermissionEngine>? _logger;

    private readonly ConcurrentDictionary<string, ApprovalDecision> _persistentRules = new(StringComparer.OrdinalIgnoreCase);
    private IApprovalPromptHandler? _promptHandler;

    public PermissionEngine(
        IPermissionRuleRepository? ruleRepository = null,
        IPermissionAuditLogger? auditLogger = null,
        ITaskRepository? taskRepository = null,
        ILogger<PermissionEngine>? logger = null)
    {
        _ruleRepository = ruleRepository;
        _auditLogger = auditLogger;
        _taskRepository = taskRepository;
        _logger = logger;

        LoadPersistentRules();
    }

    private void LoadPersistentRules()
    {
        if (_ruleRepository == null) return;

        try
        {
            var rules = _ruleRepository.LoadRulesAsync().GetAwaiter().GetResult();
            foreach (var kvp in rules)
            {
                _persistentRules[kvp.Key] = kvp.Value;
            }
            _logger?.LogInformation("Loaded {Count} persistent permission rules into engine cache.", _persistentRules.Count);
        }
        catch (Exception ex)
        {
            // Correction 8: Fail closed on store failure. Never assume allow decisions!
            _logger?.LogError(ex, "Failed to load persistent permission rules from repository. Failing closed with empty cache.");
        }
    }

    public async Task ReloadRulesAsync(CancellationToken cancellationToken = default)
    {
        if (_ruleRepository == null) return;

        try
        {
            var rules = await _ruleRepository.LoadRulesAsync(cancellationToken);
            _persistentRules.Clear();
            foreach (var kvp in rules)
            {
                _persistentRules[kvp.Key] = kvp.Value;
            }
            _logger?.LogInformation("Reloaded {Count} persistent permission rules into engine cache.", _persistentRules.Count);
        }
        catch (Exception ex)
        {
            // Correction 8: Fail closed on store failure. Never assume allow decisions!
            _logger?.LogError(ex, "Failed to reload persistent permission rules from repository. Failing closed with empty cache.");
            _persistentRules.Clear();
        }
    }

    public void SetPromptHandler(IApprovalPromptHandler promptHandler)
    {
        _promptHandler = promptHandler;
    }

    public Task<PermissionEvaluationResult> EvaluateToolExecutionAsync(
        string taskId,
        ITool tool,
        ToolCall call,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Fail-closed validation for missing or invalid tool
        if (tool == null)
        {
            _logger?.LogWarning("Permission check failed closed: tool reference is null.");
            return Task.FromResult(PermissionEvaluationResult.Denied("Security Policy: Tool reference is null."));
        }

        var scopeKey = ExtractScopeKey(tool, call);

        // Level 0: Informational. Auto-allow by default.
        if (tool.RiskLevel == ToolRiskLevel.Informational)
        {
            _logger?.LogDebug("Tool '{ToolId}' (Scope: '{ScopeKey}') auto-allowed (Risk: Informational).", tool.Id, scopeKey);
            return Task.FromResult(PermissionEvaluationResult.Allowed("Informational operations are auto-allowed."));
        }

        // Level 1: Low-risk reversible. Auto-allow unless explicitly denied.
        if (tool.RiskLevel == ToolRiskLevel.LowRiskReversible)
        {
            // Correction 3: Level 1 is auto-allowed according to risk policy. Check only if an explicit Deny exists.
            if (TryGetPersistentDecision(scopeKey, tool.Id, out var decision) && decision == ApprovalDecision.Deny)
            {
                _logger?.LogWarning("Low-risk tool '{ToolId}' denied via explicit Deny rule.", tool.Id);
                return Task.FromResult(PermissionEvaluationResult.Denied($"Tool '{tool.Id}' is denied by user permission setting."));
            }

            _logger?.LogDebug("Tool '{ToolId}' (Scope: '{ScopeKey}') auto-allowed (Risk: LowRiskReversible).", tool.Id, scopeKey);
            return Task.FromResult(PermissionEvaluationResult.Allowed("Low-risk reversible operation allowed."));
        }

        // Level 2: Sensitive operations.
        if (tool.RiskLevel == ToolRiskLevel.Sensitive)
        {
            // Correction 2: Scoped rule check.
            if (TryGetPersistentDecision(scopeKey, tool.Id, out var decision))
            {
                if (decision == ApprovalDecision.AlwaysAllow)
                {
                    _logger?.LogInformation("Sensitive tool '{ToolId}' allowed via narrow AlwaysAllow rule '{ScopeKey}'.", tool.Id, scopeKey);
                    return Task.FromResult(PermissionEvaluationResult.Allowed("Allowed via existing user permission rule."));
                }
                if (decision == ApprovalDecision.Deny)
                {
                    _logger?.LogWarning("Sensitive tool '{ToolId}' denied via Deny rule '{ScopeKey}'.", tool.Id, scopeKey);
                    return Task.FromResult(PermissionEvaluationResult.Denied("Denied by existing user permission rule."));
                }
            }

            var request = CreateApprovalRequest(taskId, tool, call, scopeKey, isReversible: false, externalDisclosure: null);
            return Task.FromResult(PermissionEvaluationResult.NeedsPrompt(request, "Sensitive operations require explicit user approval."));
        }

        // Level 3: High-risk operations (destructive file actions, shell execution).
        // Per 05_SECURITY_AND_PERMISSIONS.md, Level 3 operations MUST ALWAYS prompt the user.
        {
            var request = CreateApprovalRequest(
                taskId,
                tool,
                call,
                scopeKey,
                isReversible: false,
                externalDisclosure: "High-risk system action requiring explicit one-time confirmation.");

            _logger?.LogWarning("High-risk tool '{ToolId}' triggered permission prompt for task '{TaskId}'.", tool.Id, taskId);
            return Task.FromResult(PermissionEvaluationResult.NeedsPrompt(request, "High-risk operations always require explicit confirmation."));
        }
    }

    public async Task<ApprovalDecisionResult> RequestApprovalAsync(
        ApprovalRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var stopwatch = Stopwatch.StartNew();

        // Check if request is already expired
        if (DateTimeOffset.UtcNow >= request.ExpiresAt)
        {
            stopwatch.Stop();
            var timedOutResult = ApprovalDecisionResult.TimedOut("Approval request expired prior to presentation.");
            await RecordApprovalAuditAsync(request, timedOutResult, stopwatch.Elapsed, "System", CancellationToken.None);
            return timedOutResult;
        }

        // Check if cancellation already requested
        if (cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            var cancelledResult = ApprovalDecisionResult.Cancelled("Approval request cancelled by user prior to presentation.");
            await RecordApprovalAuditAsync(request, cancelledResult, stopwatch.Elapsed, "User", CancellationToken.None);
            return cancelledResult;
        }

        // Correction 6: Log ApprovalRequested task event in task repository before presenting prompt
        await RecordApprovalRequestedEventAsync(request, cancellationToken);

        // If no prompt handler is registered (headless/test without prompt handler), fail closed
        if (_promptHandler == null)
        {
            stopwatch.Stop();
            _logger?.LogWarning("No IApprovalPromptHandler registered. Failing closed on approval request for tool '{ToolId}'.", request.ToolId);
            var noHandlerResult = ApprovalDecisionResult.Denied("No approval prompt handler registered; failing closed.");
            await RecordApprovalAuditAsync(request, noHandlerResult, stopwatch.Elapsed, "System", CancellationToken.None);
            return noHandlerResult;
        }

        // Correction 5: Race-safe terminal outcome using atomic state transition
        // Linked timeout with request expiration and caller cancellation token
        var remainingTime = request.ExpiresAt - DateTimeOffset.UtcNow;
        if (remainingTime <= TimeSpan.Zero)
        {
            remainingTime = TimeSpan.FromMilliseconds(10);
        }

        using var timeoutCts = new CancellationTokenSource(remainingTime);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        // Terminal state coordinator
        int outcomeDetermined = 0; // 0 = pending, 1 = completed
        ApprovalDecisionResult terminalResult;

        try
        {
            var promptTask = _promptHandler.RequestApprovalAsync(request, linkedCts.Token);
            var completedTask = await Task.WhenAny(promptTask, Task.Delay(remainingTime, linkedCts.Token));

            if (completedTask == promptTask)
            {
                var handlerResult = await promptTask;
                if (Interlocked.CompareExchange(ref outcomeDetermined, 1, 0) == 0)
                {
                    terminalResult = handlerResult;
                }
                else
                {
                    terminalResult = ApprovalDecisionResult.TimedOut();
                }
            }
            else if (cancellationToken.IsCancellationRequested)
            {
                if (Interlocked.CompareExchange(ref outcomeDetermined, 1, 0) == 0)
                {
                    terminalResult = ApprovalDecisionResult.Cancelled("Approval prompt cancelled by user.");
                }
                else
                {
                    terminalResult = ApprovalDecisionResult.Cancelled();
                }
            }
            else
            {
                if (Interlocked.CompareExchange(ref outcomeDetermined, 1, 0) == 0)
                {
                    terminalResult = ApprovalDecisionResult.TimedOut("Approval prompt expired without user response.");
                }
                else
                {
                    terminalResult = ApprovalDecisionResult.TimedOut();
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (Interlocked.CompareExchange(ref outcomeDetermined, 1, 0) == 0)
            {
                terminalResult = ApprovalDecisionResult.Cancelled("Approval prompt cancelled by caller.");
            }
            else
            {
                terminalResult = ApprovalDecisionResult.Cancelled();
            }
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            if (Interlocked.CompareExchange(ref outcomeDetermined, 1, 0) == 0)
            {
                terminalResult = ApprovalDecisionResult.TimedOut("Approval prompt timed out.");
            }
            else
            {
                terminalResult = ApprovalDecisionResult.TimedOut();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Exception in approval prompt handler. Failing closed.");
            terminalResult = ApprovalDecisionResult.Denied($"Prompt handler failure: {ex.Message}");
        }

        stopwatch.Stop();

        // Correction 1 & 4: AlwaysAllow persistence only for Level 1 & Level 2!
        if (terminalResult.Outcome == ApprovalOutcome.Approved && terminalResult.Decision == ApprovalDecision.AlwaysAllow)
        {
            if (request.RiskLevel == ToolRiskLevel.HighRisk)
            {
                // Correction 1: Fail closed with denied result and audit policy violation. Do NOT throw InvalidOperationException.
                _logger?.LogWarning("Policy Violation: User attempted AlwaysAllow on High-Risk tool '{ToolId}'. Failing closed.", request.ToolId);
                terminalResult = ApprovalDecisionResult.Denied("Policy Violation: High-risk operations cannot be granted blanket AlwaysAllow.");
            }
            else
            {
                // Persist narrow rule
                await RecordDecisionAsync(request.ScopeKey, request.ToolId, ApprovalDecision.AlwaysAllow, request.RiskLevel, CancellationToken.None);
            }
        }

        // Correction 6: Record ApprovalDecided audit events
        var actor = terminalResult.Outcome == ApprovalOutcome.TimedOut ? "System" : "User";
        await RecordApprovalAuditAsync(request, terminalResult, stopwatch.Elapsed, actor, CancellationToken.None);

        return terminalResult;
    }

    public async Task<bool> RecordDecisionAsync(
        string scopeKey,
        string toolId,
        ApprovalDecision decision,
        ToolRiskLevel riskLevel,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scopeKey);
        ArgumentNullException.ThrowIfNull(toolId);

        // Correction 1: Fail closed for High-Risk AlwaysAllow without crashing
        if (riskLevel == ToolRiskLevel.HighRisk && decision == ApprovalDecision.AlwaysAllow)
        {
            _logger?.LogWarning("Policy Violation: Attempted to record AlwaysAllow on High-Risk tool '{ToolId}'. Rejected.", toolId);
            return false;
        }

        _persistentRules[scopeKey] = decision;

        if (_ruleRepository != null)
        {
            try
            {
                await _ruleRepository.SaveRuleAsync(scopeKey, toolId, decision, riskLevel, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to persist rule to repository for '{ScopeKey}'.", scopeKey);
                return false;
            }
        }

        _logger?.LogInformation("Recorded permission decision: Scope='{ScopeKey}', Decision='{Decision}'.", scopeKey, decision);
        return true;
    }

    public void RecordDecision(string toolId, ApprovalDecision decision)
    {
        // Default scope fallback for legacy/test callers
        var scopeKey = PermissionScopeKey.Build(toolId, null, null);
        _persistentRules[scopeKey] = decision;
        _persistentRules[toolId] = decision;
        _logger?.LogInformation("Recorded permission decision for tool '{ToolId}': {Decision}", toolId, decision);
    }

    public async Task<bool> RevokeDecisionAsync(string scopeKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scopeKey);

        _persistentRules.TryRemove(scopeKey, out _);

        if (_ruleRepository != null)
        {
            return await _ruleRepository.DeleteRuleAsync(scopeKey, cancellationToken);
        }

        return true;
    }

    public IReadOnlyDictionary<string, ApprovalDecision> GetPersistentDecisions()
    {
        return new Dictionary<string, ApprovalDecision>(_persistentRules, StringComparer.OrdinalIgnoreCase);
    }

    private bool TryGetPersistentDecision(string scopeKey, string toolId, out ApprovalDecision decision)
    {
        // 1. Check narrow scope key first (e.g. "open_app:launch:notepad")
        if (_persistentRules.TryGetValue(scopeKey, out decision))
        {
            return true;
        }

        // 2. Check tool-wide scope key (e.g. "open_app:*:*")
        var toolWildcardKey = PermissionScopeKey.Build(toolId, null, null);
        if (_persistentRules.TryGetValue(toolWildcardKey, out decision))
        {
            return true;
        }

        // 3. Fallback check by raw tool ID
        if (_persistentRules.TryGetValue(toolId, out decision))
        {
            return true;
        }

        decision = default;
        return false;
    }

    private static string ExtractScopeKey(ITool tool, ToolCall call)
    {
        string? resource = null;
        string? action = null;

        if (tool.Id.Equals("open_app", StringComparison.OrdinalIgnoreCase))
        {
            action = "launch";
        }
        else if (tool.Id.Equals("browser_page_read", StringComparison.OrdinalIgnoreCase))
        {
            action = "read";
        }
        else if (tool.Id.Equals("browser_extract", StringComparison.OrdinalIgnoreCase))
        {
            action = "extract";
        }
        else if (tool.Id.Equals("browser_search", StringComparison.OrdinalIgnoreCase))
        {
            action = "search";
            resource = "web";
        }
        else if (tool.Id.Equals("app_list", StringComparison.OrdinalIgnoreCase))
        {
            action = "read";
        }
        else if (tool.Id.Equals("recent_apps", StringComparison.OrdinalIgnoreCase))
        {
            action = "read";
        }
        else if (tool.Id.Equals("window_focus", StringComparison.OrdinalIgnoreCase))
        {
            action = "focus";
        }
        else if (tool.Id.Equals("window_ui_interact", StringComparison.OrdinalIgnoreCase))
        {
            action = "interact";
        }
        else if (tool.Id.Equals("memory_save", StringComparison.OrdinalIgnoreCase))
        {
            action = "save";
        }
        else if (tool.Id.Equals("memory_query", StringComparison.OrdinalIgnoreCase))
        {
            action = "read";
        }
        else if (tool.Id.Equals("memory_delete", StringComparison.OrdinalIgnoreCase))
        {
            action = "delete";
            resource = "*"; // Reuse conventions, do not invent per-memory-ID permissions
        }
        else if (tool.Id.Equals("memory_clear", StringComparison.OrdinalIgnoreCase))
        {
            action = "clear";
        }
        else if (tool.Id.Equals("capture_screen", StringComparison.OrdinalIgnoreCase))
        {
            action = "capture";
            resource = "screen";
        }
        else if (tool.Id.Equals("analyze_screen", StringComparison.OrdinalIgnoreCase))
        {
            action = "analyze";
            resource = "screen";
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(call.ArgumentsJson))
            {
                using var doc = JsonDocument.Parse(call.ArgumentsJson);
                if (action == null && doc.RootElement.TryGetProperty("action", out var actElem))
                {
                    action = actElem.GetString();
                }

                if (doc.RootElement.TryGetProperty("app_name", out var appElem))
                {
                    resource = appElem.GetString();
                }
                else if (doc.RootElement.TryGetProperty("hwnd", out var hwndElem))
                {
                    resource = hwndElem.GetString();
                }
                else if (doc.RootElement.TryGetProperty("url", out var urlElem))
                {
                    var rawUrl = urlElem.GetString();
                    if (!string.IsNullOrWhiteSpace(rawUrl))
                    {
                        if (Uri.TryCreate(rawUrl, UriKind.Absolute, out var parsedUri))
                        {
                            resource = parsedUri.Host.ToLowerInvariant();
                        }
                        else
                        {
                            resource = rawUrl.ToLowerInvariant();
                        }
                    }
                }
                else if (doc.RootElement.TryGetProperty("category", out var catElem))
                {
                    resource = catElem.GetString();
                }
                else if (doc.RootElement.TryGetProperty("resource", out var resElem))
                {
                    resource = resElem.GetString();
                }
                else if (doc.RootElement.TryGetProperty("target", out var targetElem))
                {
                    resource = targetElem.GetString();
                }
            }
        }
        catch { }

        return PermissionScopeKey.Build(tool.Id, action, resource);
    }

    private static ApprovalRequest CreateApprovalRequest(
        string taskId,
        ITool tool,
        ToolCall call,
        string scopeKey,
        bool isReversible,
        string? externalDisclosure)
    {
        var now = DateTimeOffset.UtcNow;
        var timeout = (tool.DefaultTimeout > TimeSpan.Zero && tool.DefaultTimeout < TimeSpan.FromSeconds(60))
            ? tool.DefaultTimeout
            : TimeSpan.FromSeconds(60);

        // Correction 7: Redact secrets in arguments and description
        var safeArgs = SecretRedactor.Redact(call.ArgumentsJson);
        var safeDesc = SecretRedactor.Redact(tool.Description);
        var safeResource = SecretRedactor.Redact(tool.Name);

        return new ApprovalRequest(
            RequestId: Guid.NewGuid().ToString("N"),
            TaskId: string.IsNullOrWhiteSpace(taskId) ? null : taskId,
            ToolId: tool.Id,
            ScopeKey: scopeKey,
            RiskLevel: tool.RiskLevel,
            ActionDescription: safeDesc,
            AffectedResource: safeResource,
            IsReversible: isReversible,
            ExternalDataDisclosureExplanation: externalDisclosure,
            SanitizedArguments: safeArgs,
            CreatedAt: now,
            ExpiresAt: now.Add(timeout),
            Timeout: timeout
        );
    }

    private async Task RecordApprovalRequestedEventAsync(ApprovalRequest request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.TaskId) && _taskRepository != null)
        {
            try
            {
                var details = new
                {
                    request_id = request.RequestId,
                    tool_id = request.ToolId,
                    scope_key = request.ScopeKey,
                    risk_level = request.RiskLevel.ToString(),
                    action_description = SecretRedactor.Redact(request.ActionDescription),
                    affected_resource = SecretRedactor.Redact(request.AffectedResource),
                    expires_at = request.ExpiresAt.ToString("O")
                };

                var evt = new TaskEvent(
                    taskId: request.TaskId,
                    eventType: TaskEventType.ApprovalRequested,
                    message: $"Approval requested for tool '{request.ToolId}' ({request.RiskLevel})",
                    detailsJson: JsonSerializer.Serialize(details)
                );

                await _taskRepository.AddEventAsync(evt, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to record ApprovalRequested event for task '{TaskId}'.", request.TaskId);
            }
        }
    }

    private async Task RecordApprovalAuditAsync(
        ApprovalRequest request,
        ApprovalDecisionResult result,
        TimeSpan duration,
        string actor,
        CancellationToken cancellationToken)
    {
        // 1. Task Repository event (Correction 6: ApprovalDecided)
        if (!string.IsNullOrWhiteSpace(request.TaskId) && _taskRepository != null)
        {
            try
            {
                var details = new
                {
                    request_id = request.RequestId,
                    tool_id = request.ToolId,
                    scope_key = request.ScopeKey,
                    outcome = result.Outcome.ToString(),
                    decision = result.Decision?.ToString(),
                    reason = SecretRedactor.Redact(result.Reason),
                    actor = actor,
                    duration_ms = duration.TotalMilliseconds
                };

                var evt = new TaskEvent(
                    taskId: request.TaskId,
                    eventType: TaskEventType.ApprovalDecided,
                    message: $"Approval outcome for tool '{request.ToolId}': {result.Outcome} ({result.Decision}) by {actor}",
                    detailsJson: JsonSerializer.Serialize(details)
                );

                await _taskRepository.AddEventAsync(evt, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to record ApprovalDecided event for task '{TaskId}'.", request.TaskId);
            }
        }

        // 2. Permission Audit Logger
        if (_auditLogger != null)
        {
            try
            {
                var auditRecord = new PermissionAuditRecord(
                    RequestId: request.RequestId,
                    TaskId: request.TaskId,
                    ToolId: request.ToolId,
                    ScopeKey: request.ScopeKey,
                    RiskLevel: request.RiskLevel,
                    Outcome: result.Outcome,
                    Decision: result.Decision,
                    Actor: actor,
                    Timestamp: DateTimeOffset.UtcNow,
                    Duration: duration,
                    SanitizedDetails: SecretRedactor.Redact(result.Reason)
                );

                await _auditLogger.LogPermissionEventAsync(auditRecord, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to record permission audit record for request '{RequestId}'.", request.RequestId);
            }
        }
    }
}
