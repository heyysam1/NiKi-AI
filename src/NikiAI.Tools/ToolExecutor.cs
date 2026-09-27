using System.Diagnostics;
using Microsoft.Extensions.Logging;
using NikiAI.Core.Agent;
using NikiAI.Core.Logging;
using NikiAI.Core.Security;
using NikiAI.Core.Tasks;
using NikiAI.Core.Tools;

namespace NikiAI.Tools;

/// <summary>
/// Tool execution pipeline coordinator contract.
/// </summary>
public interface IToolExecutor
{
    Task<ToolResult> ExecuteAsync(
        ToolCall call,
        string? taskId = null,
        CancellationToken cancellationToken = default);

    Task<AgentToolResult> ExecuteAgentToolCallAsync(
        AgentToolCall call,
        string? taskId = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Orchestrates the complete tool execution lifecycle:
/// 1. Registry lookup
/// 2. Schema / input validation
/// 3. Security & permission gate evaluation
/// 4. Linked timeout & cancellation management
/// 5. Execution
/// 6. Secret-redacted audit logging (task-associated or standalone)
/// </summary>
public class ToolExecutor : IToolExecutor
{
    private readonly IToolRegistry _toolRegistry;
    private readonly IPermissionEngine _permissionEngine;
    private readonly IToolAuditLogger _auditLogger;
    private readonly ITaskRepository? _taskRepository;
    private readonly ILogger<ToolExecutor>? _logger;

    public ToolExecutor(
        IToolRegistry toolRegistry,
        IPermissionEngine permissionEngine,
        IToolAuditLogger auditLogger,
        ITaskRepository? taskRepository = null,
        ILogger<ToolExecutor>? logger = null)
    {
        _toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
        _permissionEngine = permissionEngine ?? throw new ArgumentNullException(nameof(permissionEngine));
        _auditLogger = auditLogger ?? throw new ArgumentNullException(nameof(auditLogger));
        _taskRepository = taskRepository;
        _logger = logger;
    }

    public async Task<ToolResult> ExecuteAsync(
        ToolCall call,
        string? taskId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(call);

        // 1. Registry Lookup
        var tool = _toolRegistry.GetTool(call.ToolId);
        if (tool == null)
        {
            var notFound = ToolResult.Failure(call.CallId, call.ToolId, $"Tool '{call.ToolId}' was not found in registry.", TimeSpan.Zero);
            await RecordAuditAsync(taskId, call, ToolRiskLevel.Informational, false, notFound, TimeSpan.Zero, cancellationToken);
            return notFound;
        }

        // 2. Schema / Input Validation (documented schema subset)
        if (!ToolInputValidator.Validate(call.ArgumentsJson, tool.InputSchemaJson, out var validationError))
        {
            var invalidResult = ToolResult.Failure(call.CallId, tool.Id, $"Input validation error: {validationError}", TimeSpan.Zero);
            await RecordAuditAsync(taskId, call, tool.RiskLevel, false, invalidResult, TimeSpan.Zero, cancellationToken);
            return invalidResult;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return ToolResult.Failure(call.CallId, tool.Id, "Tool execution was cancelled by user.", TimeSpan.Zero);
        }

        // 3. Security & Permission Evaluation
        PermissionEvaluationResult permissionResult;
        try
        {
            permissionResult = await _permissionEngine.EvaluateToolExecutionAsync(
                taskId ?? "standalone",
                tool,
                call,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ToolResult.Failure(call.CallId, tool.Id, "Tool execution was cancelled by user.", TimeSpan.Zero);
        }

        if (!permissionResult.IsAllowed)
        {
            if (permissionResult.RequiresUserPrompt && permissionResult.PromptRequest != null)
            {
                if (!string.IsNullOrWhiteSpace(taskId) && _taskRepository != null)
                {
                    try
                    {
                        await _taskRepository.TransitionStatusAsync(taskId, AgentTaskStatus.NeedsApproval, "Waiting for user approval.", cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Failed to transition task '{TaskId}' to NeedsApproval.", taskId);
                    }
                }

                ApprovalDecisionResult approvalResult;
                try
                {
                    approvalResult = await _permissionEngine.RequestApprovalAsync(
                        permissionResult.PromptRequest,
                        cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    if (!string.IsNullOrWhiteSpace(taskId) && _taskRepository != null)
                    {
                        try { await _taskRepository.TransitionStatusAsync(taskId, AgentTaskStatus.Cancelled, "Tool approval was cancelled by user.", CancellationToken.None); } catch { }
                    }
                    return ToolResult.Failure(call.CallId, tool.Id, "Tool execution was cancelled by user.", TimeSpan.Zero);
                }

                if (approvalResult.Outcome != ApprovalOutcome.Approved)
                {
                    if (!string.IsNullOrWhiteSpace(taskId) && _taskRepository != null)
                    {
                        var targetStatus = approvalResult.Outcome == ApprovalOutcome.Cancelled ? AgentTaskStatus.Cancelled : AgentTaskStatus.Failed;
                        try { await _taskRepository.TransitionStatusAsync(taskId, targetStatus, $"Tool approval was not granted ({approvalResult.Outcome}).", CancellationToken.None); } catch { }
                    }

                    string failReason = approvalResult.Outcome switch
                    {
                        ApprovalOutcome.Denied => $"Permission Denied: User denied approval for tool '{tool.Id}'.",
                        ApprovalOutcome.TimedOut => $"Permission Denied: Approval request timed out for tool '{tool.Id}'.",
                        ApprovalOutcome.Cancelled => $"Permission Denied: Approval request was cancelled for tool '{tool.Id}'.",
                        _ => $"Permission Denied: Execution was not approved for tool '{tool.Id}'."
                    };

                    return ToolResult.Failure(call.CallId, tool.Id, failReason, TimeSpan.Zero);
                }

                // Approved (AllowOnce or AlwaysAllow): transition back to Running and proceed to execution!
                if (!string.IsNullOrWhiteSpace(taskId) && _taskRepository != null)
                {
                    try
                    {
                        await _taskRepository.TransitionStatusAsync(taskId, AgentTaskStatus.Running, "User approved execution; resuming running state.", cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Failed to transition task '{TaskId}' back to Running.", taskId);
                    }
                }
            }
            else
            {
                string reason = $"Permission Denied: Execution of tool '{tool.Id}' was denied ({permissionResult.Reason}).";
                return ToolResult.Failure(call.CallId, tool.Id, reason, TimeSpan.Zero);
            }
        }

        // 4. Linked Timeout & Cancellation Setup
        using var timeoutCts = new CancellationTokenSource(tool.DefaultTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        var stopwatch = Stopwatch.StartNew();
        ToolResult result;

        try
        {
            // 5. Tool Execution
            result = await tool.ExecuteAsync(call, linkedCts.Token);
            stopwatch.Stop();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            result = ToolResult.Failure(call.CallId, tool.Id, "Tool execution was cancelled by user.", stopwatch.Elapsed);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            stopwatch.Stop();
            result = ToolResult.Failure(
                call.CallId,
                tool.Id,
                $"Tool execution timed out after {tool.DefaultTimeout.TotalSeconds} seconds.",
                stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            result = ToolResult.Failure(call.CallId, tool.Id, $"Unexpected tool execution error: {ex.Message}", stopwatch.Elapsed);
        }

        // 6. Secret-Redacted Audit Logging
        await RecordAuditAsync(taskId, call, tool.RiskLevel, false, result, stopwatch.Elapsed, CancellationToken.None);

        return result;
    }

    private async Task RecordAuditAsync(
        string? taskId,
        ToolCall call,
        ToolRiskLevel riskLevel,
        bool requiresApproval,
        ToolResult result,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        try
        {
            var auditRecord = new ToolAuditRecord(
                TaskId: taskId,
                CallId: call.CallId,
                ToolId: call.ToolId,
                SanitizedArgumentsJson: SecretRedactor.Redact(call.ArgumentsJson),
                IsSuccess: result.IsSuccess,
                SanitizedOutputJson: SecretRedactor.Redact(result.OutputJson),
                ErrorMessage: SecretRedactor.Redact(result.ErrorMessage),
                Duration: duration,
                Timestamp: DateTimeOffset.UtcNow,
                RiskLevel: riskLevel,
                RequiresApproval: requiresApproval
            );

            await _auditLogger.LogExecutionAsync(auditRecord, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to record audit record for tool call '{CallId}'.", call.CallId);
        }
    }

    public async Task<AgentToolResult> ExecuteAgentToolCallAsync(
        AgentToolCall call,
        string? taskId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(call);

        var toolCall = new ToolCall(
            call.CallId,
            call.ToolName,
            call.ArgumentsJson ?? "{}",
            DateTimeOffset.UtcNow);

        var result = await ExecuteAsync(toolCall, taskId, cancellationToken);

        return new AgentToolResult(
            call.CallId,
            call.ToolName,
            result.IsSuccess,
            result.IsSuccess
                ? (result.OutputJson ?? "{}")
                : (result.ErrorMessage ?? "Tool execution failed."),
            result.ErrorMessage);
    }
}
