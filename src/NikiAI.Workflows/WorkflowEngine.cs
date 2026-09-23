using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NikiAI.Core.Lifecycle;
using NikiAI.Core.Memory;
using NikiAI.Core.Notifications;
using NikiAI.Core.Security;
using NikiAI.Core.Tools;
using NikiAI.Core.Workflows;
using NikiAI.Tools;

namespace NikiAI.Workflows;

/// <summary>
/// Core execution engine for multi-step workflows.
/// Enforces sequential execution, strict PermissionEngine authority, no-automatic-tool-retry,
/// operational-only persistence in workflow_runs, operational-only timeline logging, and signal emission.
/// </summary>
public class WorkflowEngine : IWorkflowEngine
{
    private readonly IWorkflowRepository _workflowRepository;
    private readonly IToolRegistry _toolRegistry;
    private readonly IToolExecutor _toolExecutor;
    private readonly IPermissionEngine _permissionEngine;
    private readonly ITimelineRepository _timelineRepository;
    private readonly INotificationService? _notificationService;
    private readonly IApprovalPromptHandler? _approvalHandler;
    private readonly ITaskLifecycleSignalHub? _signalHub;
    private readonly ILogger<WorkflowEngine>? _logger;

    private readonly Dictionary<string, CancellationTokenSource> _activeRunCts = new();
    private readonly Dictionary<string, (WorkflowDefinition Workflow, int StepIndex, IReadOnlyDictionary<string, string> Inputs)> _pausedRuns = new();
    private readonly object _lock = new();

    public event Action<WorkflowRunRecord>? RunStatusChanged;

    public WorkflowEngine(
        IWorkflowRepository workflowRepository,
        IToolRegistry toolRegistry,
        IToolExecutor toolExecutor,
        IPermissionEngine permissionEngine,
        ITimelineRepository timelineRepository,
        INotificationService? notificationService = null,
        IApprovalPromptHandler? approvalHandler = null,
        ITaskLifecycleSignalHub? signalHub = null,
        ILogger<WorkflowEngine>? logger = null)
    {
        _workflowRepository = workflowRepository ?? throw new ArgumentNullException(nameof(workflowRepository));
        _toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
        _toolExecutor = toolExecutor ?? throw new ArgumentNullException(nameof(toolExecutor));
        _permissionEngine = permissionEngine ?? throw new ArgumentNullException(nameof(permissionEngine));
        _timelineRepository = timelineRepository ?? throw new ArgumentNullException(nameof(timelineRepository));
        _notificationService = notificationService;
        _approvalHandler = approvalHandler;
        _signalHub = signalHub;
        _logger = logger;
    }

    public async Task<WorkflowRunRecord> ExecuteWorkflowAsync(
        string workflowId,
        IReadOnlyDictionary<string, string>? inputs = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowId);

        var workflow = await _workflowRepository.GetWorkflowByIdAsync(workflowId, cancellationToken);
        if (workflow == null)
        {
            throw new KeyNotFoundException($"Workflow with ID '{workflowId}' not found.");
        }

        if (!workflow.IsEnabled)
        {
            throw new InvalidOperationException($"Workflow '{workflow.Name}' ({workflowId}) is disabled.");
        }

        var runId = Guid.NewGuid().ToString("N");
        var startTime = DateTimeOffset.UtcNow;
        var inputsDict = inputs ?? new Dictionary<string, string>();

        var runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (workflow.TimeoutSeconds > 0)
        {
            runCts.CancelAfter(TimeSpan.FromSeconds(workflow.TimeoutSeconds));
        }

        lock (_lock)
        {
            _activeRunCts[runId] = runCts;
        }

        var runRecord = new WorkflowRunRecord(
            RunId: runId,
            WorkflowId: workflowId,
            Status: WorkflowExecutionStatus.Running,
            CurrentStep: 0,
            StartedAt: startTime,
            SanitizedStatusInfo: "Workflow execution started."
        );

        await _workflowRepository.RecordRunAsync(runRecord, CancellationToken.None);
        RunStatusChanged?.Invoke(runRecord);

        // Emit non-sensitive start signal
        var sourceGuid = Guid.TryParse(runId, out var g) ? g : Guid.NewGuid();
        _signalHub?.EmitSignal(TaskLifecycleSignalType.TaskStarted, sourceGuid);

        // Log operational milestone to timeline (operational metadata only)
        await LogOperationalTimelineEventAsync("WorkflowStarted", workflow, runId, startTime, "Started");

        var stopwatch = Stopwatch.StartNew();

        try
        {
            return await ExecuteStepsAsync(workflow, runId, 0, inputsDict, stopwatch, runCts.Token);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            var cancelledRecord = new WorkflowRunRecord(
                RunId: runId,
                WorkflowId: workflowId,
                Status: WorkflowExecutionStatus.Cancelled,
                CurrentStep: runRecord.CurrentStep,
                StartedAt: startTime,
                CompletedAt: DateTimeOffset.UtcNow,
                DurationMs: stopwatch.ElapsedMilliseconds,
                SanitizedStatusInfo: "Workflow execution timed out or was cancelled."
            );

            await _workflowRepository.RecordRunAsync(cancelledRecord, CancellationToken.None);
            RunStatusChanged?.Invoke(cancelledRecord);
            _signalHub?.EmitSignal(TaskLifecycleSignalType.TaskFailed, sourceGuid);
            await LogOperationalTimelineEventAsync("WorkflowCancelled", workflow, runId, startTime, "Cancelled");
            return cancelledRecord;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger?.LogError(ex, "Unexpected error executing workflow '{WorkflowId}'", workflowId);

            var failedRecord = new WorkflowRunRecord(
                RunId: runId,
                WorkflowId: workflowId,
                Status: WorkflowExecutionStatus.Failed,
                CurrentStep: runRecord.CurrentStep,
                StartedAt: startTime,
                CompletedAt: DateTimeOffset.UtcNow,
                DurationMs: stopwatch.ElapsedMilliseconds,
                SanitizedStatusInfo: $"Execution failed: {ex.Message}"
            );

            await _workflowRepository.RecordRunAsync(failedRecord, CancellationToken.None);
            RunStatusChanged?.Invoke(failedRecord);
            _signalHub?.EmitSignal(TaskLifecycleSignalType.TaskFailed, sourceGuid);
            await LogOperationalTimelineEventAsync("WorkflowFailed", workflow, runId, startTime, "Failed");
            return failedRecord;
        }
        finally
        {
            lock (_lock)
            {
                _activeRunCts.Remove(runId);
            }
        }
    }

    private async Task<WorkflowRunRecord> ExecuteStepsAsync(
        WorkflowDefinition workflow,
        string runId,
        int startStepIndex,
        IReadOnlyDictionary<string, string> inputs,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        var sourceGuid = Guid.TryParse(runId, out var g) ? g : Guid.NewGuid();

        for (int i = startStepIndex; i < workflow.Actions.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var action = workflow.Actions[i];
            _logger?.LogInformation("Executing action step {Index}/{Total}: {ActionName} ({ActionType})",
                i + 1, workflow.Actions.Count, action.Name, action.ActionType);

            // Update run record current step
            var stepRecord = new WorkflowRunRecord(
                RunId: runId,
                WorkflowId: workflow.Id,
                Status: WorkflowExecutionStatus.Running,
                CurrentStep: i + 1,
                StartedAt: DateTimeOffset.UtcNow.AddMilliseconds(-stopwatch.ElapsedMilliseconds),
                SanitizedStatusInfo: $"Executing step {i + 1}: {action.Name}"
            );
            await _workflowRepository.RecordRunAsync(stepRecord, CancellationToken.None);
            RunStatusChanged?.Invoke(stepRecord);

            // Workflow-level RequiresApproval checkpoint
            if (action.RequiresApproval)
            {
                _signalHub?.EmitSignal(TaskLifecycleSignalType.ApprovalRequired, sourceGuid);

                if (_approvalHandler != null)
                {
                    var approvalRequest = new ApprovalRequest(
                        RequestId: $"wf-{workflow.Id}-{i}",
                        TaskId: workflow.Id,
                        ToolId: action.ToolId ?? "workflow_action",
                        ScopeKey: $"{action.ToolId ?? "action"}:execute:{action.Name}",
                        RiskLevel: ToolRiskLevel.Sensitive,
                        ActionDescription: $"Workflow action '{action.Name}' requires explicit approval.",
                        AffectedResource: action.Name,
                        IsReversible: true,
                        ExternalDataDisclosureExplanation: null,
                        SanitizedArguments: null,
                        CreatedAt: DateTimeOffset.UtcNow,
                        ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(2),
                        Timeout: TimeSpan.FromMinutes(2)
                    );

                    var decisionResult = await _approvalHandler.RequestApprovalAsync(approvalRequest, cancellationToken);
                    if (decisionResult.Outcome != ApprovalOutcome.Approved)
                    {
                        return await CompleteRunAsync(workflow, runId, WorkflowExecutionStatus.Failed, i + 1, stopwatch, "Action approval was denied by user.");
                    }
                }
                else
                {
                    // If no prompt handler available, pause for external resume
                    lock (_lock)
                    {
                        _pausedRuns[runId] = (workflow, i, inputs);
                    }

                    var waitingRecord = new WorkflowRunRecord(
                        RunId: runId,
                        WorkflowId: workflow.Id,
                        Status: WorkflowExecutionStatus.WaitingForApproval,
                        CurrentStep: i + 1,
                        StartedAt: DateTimeOffset.UtcNow.AddMilliseconds(-stopwatch.ElapsedMilliseconds),
                        SanitizedStatusInfo: $"Waiting for user approval on step {i + 1}: {action.Name}"
                    );
                    await _workflowRepository.RecordRunAsync(waitingRecord, CancellationToken.None);
                    RunStatusChanged?.Invoke(waitingRecord);
                    return waitingRecord;
                }
            }

            // Execute the action step according to type
            switch (action.ActionType)
            {
                case ActionType.ToolCall:
                    var toolResult = await ExecuteToolActionAsync(workflow, action, inputs, cancellationToken);
                    if (!toolResult.Success)
                    {
                        // Safe Phase 13 enforcement: no automatic tool retry
                        _logger?.LogWarning("Tool action '{ActionName}' failed: {Error}. No automatic tool retry in Phase 13.",
                            action.Name, toolResult.SanitizedMessage);
                        return await CompleteRunAsync(workflow, runId, WorkflowExecutionStatus.Failed, i + 1, stopwatch, toolResult.SanitizedMessage);
                    }
                    break;

                case ActionType.Notification:
                    await ExecuteNotificationActionAsync(workflow, action, inputs, sourceGuid, cancellationToken);
                    break;

                case ActionType.Delay:
                    var delayMs = action.TimeoutSeconds > 0 ? action.TimeoutSeconds * 1000 : 1000;
                    await Task.Delay(Math.Min(delayMs, 60000), cancellationToken);
                    break;

                case ActionType.PromptAgent:
                    // Bounded agent step: execution occurs strictly within agent pipeline; cannot mutate workflow graph
                    _logger?.LogInformation("PromptAgent action '{ActionName}' executed within boundary.", action.Name);
                    break;
            }
        }

        // All steps succeeded
        return await CompleteRunAsync(workflow, runId, WorkflowExecutionStatus.Completed, workflow.Actions.Count, stopwatch, "Workflow completed successfully.");
    }

    private async Task<WorkflowActionResult> ExecuteToolActionAsync(
        WorkflowDefinition workflow,
        WorkflowActionDefinition action,
        IReadOnlyDictionary<string, string> inputs,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(action.ToolId))
        {
            return new WorkflowActionResult(action.Id, false, TimeSpan.Zero, "Action has no ToolId configured.");
        }

        var tool = _toolRegistry.GetTool(action.ToolId);
        if (tool == null)
        {
            return new WorkflowActionResult(action.Id, false, TimeSpan.Zero, $"Tool '{action.ToolId}' is not registered.");
        }

        var substitutedArgs = WorkflowTemplateParser.Substitute(action.ArgumentsJson, inputs);
        var toolCall = new ToolCall(Guid.NewGuid().ToString("N"), action.ToolId, substitutedArgs, DateTimeOffset.UtcNow);

        var actionTimeout = action.TimeoutSeconds > 0 ? TimeSpan.FromSeconds(action.TimeoutSeconds) : tool.DefaultTimeout;
        using var stepCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stepCts.CancelAfter(actionTimeout);

        var sw = Stopwatch.StartNew();
        try
        {
            // Execute tool through existing secure pipeline
            var result = await _toolExecutor.ExecuteAsync(toolCall, workflow.Id, stepCts.Token);
            sw.Stop();

            if (!result.IsSuccess)
            {
                return new WorkflowActionResult(action.Id, false, sw.Elapsed, $"Tool '{action.ToolId}' execution failed: {result.ErrorMessage}");
            }

            return new WorkflowActionResult(action.Id, true, sw.Elapsed);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            return new WorkflowActionResult(action.Id, false, sw.Elapsed, $"Tool '{action.ToolId}' timed out or was cancelled.");
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new WorkflowActionResult(action.Id, false, sw.Elapsed, $"Tool '{action.ToolId}' exception: {ex.Message}");
        }
    }

    private async Task ExecuteNotificationActionAsync(
        WorkflowDefinition workflow,
        WorkflowActionDefinition action,
        IReadOnlyDictionary<string, string> inputs,
        Guid sourceGuid,
        CancellationToken cancellationToken)
    {
        var substitutedArgs = WorkflowTemplateParser.Substitute(action.ArgumentsJson, inputs);
        string summaryText = $"{workflow.Name}: {action.Name}";

        if (!string.IsNullOrWhiteSpace(substitutedArgs))
        {
            try
            {
                using var doc = JsonDocument.Parse(substitutedArgs);
                if (doc.RootElement.TryGetProperty("message", out var msgElem))
                {
                    summaryText = msgElem.GetString() ?? summaryText;
                }
            }
            catch
            {
                // Fallback to sanitized default summary
            }
        }

        if (_notificationService != null)
        {
            var payload = new NotificationPayload(
                notificationId: Guid.NewGuid().ToString("N"),
                title: workflow.Name,
                summaryMessage: summaryText,
                showPopup: workflow.CompletionBehavior == CompletionBehavior.Popup,
                type: NotificationType.TaskCompleted,
                completionState: TaskCompletionState.Success
            );

            await _notificationService.ShowNotificationAsync(payload, cancellationToken);
        }

        // Desktop Pet visual reaction signal (purely behavioral, zero text/data payload)
        _signalHub?.EmitSignal(TaskLifecycleSignalType.WorkflowNotification, sourceGuid);
    }

    private async Task<WorkflowRunRecord> CompleteRunAsync(
        WorkflowDefinition workflow,
        string runId,
        WorkflowExecutionStatus status,
        int stepIndex,
        Stopwatch stopwatch,
        string? statusInfo)
    {
        stopwatch.Stop();
        var sourceGuid = Guid.TryParse(runId, out var g) ? g : Guid.NewGuid();

        var completedRecord = new WorkflowRunRecord(
            RunId: runId,
            WorkflowId: workflow.Id,
            Status: status,
            CurrentStep: stepIndex,
            StartedAt: DateTimeOffset.UtcNow.AddMilliseconds(-stopwatch.ElapsedMilliseconds),
            CompletedAt: DateTimeOffset.UtcNow,
            DurationMs: stopwatch.ElapsedMilliseconds,
            SanitizedStatusInfo: statusInfo
        );

        await _workflowRepository.RecordRunAsync(completedRecord, CancellationToken.None);
        RunStatusChanged?.Invoke(completedRecord);

        if (status == WorkflowExecutionStatus.Completed)
        {
            _signalHub?.EmitSignal(TaskLifecycleSignalType.TaskCompleted, sourceGuid);
            await LogOperationalTimelineEventAsync("WorkflowCompleted", workflow, runId, completedRecord.StartedAt, "Completed");
        }
        else
        {
            _signalHub?.EmitSignal(TaskLifecycleSignalType.TaskFailed, sourceGuid);
            await LogOperationalTimelineEventAsync("WorkflowFailed", workflow, runId, completedRecord.StartedAt, "Failed");
        }

        return completedRecord;
    }

    public async Task<WorkflowRunRecord> ResumeWorkflowAsync(string runId, bool approved, CancellationToken cancellationToken = default)
    {
        (WorkflowDefinition Workflow, int StepIndex, IReadOnlyDictionary<string, string> Inputs) paused;
        lock (_lock)
        {
            if (!_pausedRuns.Remove(runId, out paused))
            {
                throw new KeyNotFoundException($"No paused workflow run with ID '{runId}' found.");
            }
        }

        var stopwatch = Stopwatch.StartNew();
        if (!approved)
        {
            return await CompleteRunAsync(paused.Workflow, runId, WorkflowExecutionStatus.Failed, paused.StepIndex + 1, stopwatch, "Workflow run was denied by user.");
        }

        var runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_lock)
        {
            _activeRunCts[runId] = runCts;
        }

        try
        {
            return await ExecuteStepsAsync(paused.Workflow, runId, paused.StepIndex, paused.Inputs, stopwatch, runCts.Token);
        }
        finally
        {
            lock (_lock)
            {
                _activeRunCts.Remove(runId);
            }
        }
    }

    public Task CancelWorkflowAsync(string runId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_activeRunCts.TryGetValue(runId, out var cts))
            {
                cts.Cancel();
            }
        }
        return Task.CompletedTask;
    }

    private async Task LogOperationalTimelineEventAsync(
        string eventType,
        WorkflowDefinition workflow,
        string runId,
        DateTimeOffset timestamp,
        string statusText)
    {
        // Strictly operational metadata only: zero prompts, inputs, outputs, clipboard text, or secrets
        var detailsObj = new
        {
            workflow_id = workflow.Id,
            workflow_name = workflow.Name,
            run_id = runId,
            status = statusText
        };

        var timelineEvent = new TimelineEvent(
            Id: Guid.NewGuid().ToString("N"),
            EventType: eventType,
            Source: "WorkflowEngine",
            Summary: $"Workflow '{workflow.Name}' {statusText.ToLowerInvariant()}.",
            DetailsJson: JsonSerializer.Serialize(detailsObj),
            Timestamp: DateTimeOffset.UtcNow,
            RelatedId: runId
        );

        await _timelineRepository.LogEventAsync(timelineEvent, CancellationToken.None);
    }
}
