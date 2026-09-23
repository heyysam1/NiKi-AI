namespace NikiAI.Core.Workflows;

/// <summary>
/// Trigger types supported by the Workflow subsystem.
/// </summary>
public enum TriggerType
{
    Manual = 0,
    Scheduled = 1,
    Hotkey = 2,
    Event = 3
}

/// <summary>
/// Action types supported in a workflow sequence.
/// </summary>
public enum ActionType
{
    ToolCall = 0,
    PromptAgent = 1,
    Delay = 2,
    Notification = 3
}

/// <summary>
/// Completion behavior when a workflow finishes execution.
/// </summary>
public enum CompletionBehavior
{
    Notify = 0,
    Popup = 1,
    Silent = 2
}

/// <summary>
/// Execution status of a workflow run.
/// </summary>
public enum WorkflowExecutionStatus
{
    Pending = 0,
    Running = 1,
    WaitingForApproval = 2,
    Completed = 3,
    Failed = 4,
    Cancelled = 5
}

/// <summary>
/// Definition of an action step within a workflow.
/// In Phase 13, tool actions have no automatic retry and no user-controlled idempotency flags.
/// </summary>
public record WorkflowActionDefinition(
    string Id,
    string Name,
    ActionType ActionType,
    string? ToolId = null,
    string? ArgumentsJson = null,
    bool RequiresApproval = false,
    string? Condition = null,
    int TimeoutSeconds = 30
);

/// <summary>
/// Definition of a multi-step workflow.
/// In Phase 13, RetryPolicy is completely removed from the workflow model.
/// </summary>
public record WorkflowDefinition(
    string Id,
    string Name,
    string Description,
    string Category,
    TriggerType Trigger,
    string? TriggerConfigJson,
    IReadOnlyList<string> Inputs,
    IReadOnlyList<WorkflowActionDefinition> Actions,
    int TimeoutSeconds,
    CompletionBehavior CompletionBehavior,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool IsEnabled = true
);

/// <summary>
/// Operational metadata persistence record for workflow execution runs.
/// Persisted in workflow_runs table.
/// Strictly excludes tool arguments, results, inputs, outputs, prompts, clipboard contents, and secrets.
/// </summary>
public record WorkflowRunRecord(
    string RunId,
    string WorkflowId,
    WorkflowExecutionStatus Status,
    int CurrentStep,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt = null,
    long? DurationMs = null,
    string? SanitizedStatusInfo = null
);

/// <summary>
/// In-memory execution state tracking current run progress.
/// </summary>
public record WorkflowExecutionState(
    string RunId,
    string WorkflowId,
    WorkflowExecutionStatus Status,
    int CurrentStep,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt = null,
    string? SanitizedStatusInfo = null
);

/// <summary>
/// In-memory result of an individual workflow action step.
/// </summary>
public record WorkflowActionResult(
    string ActionId,
    bool Success,
    TimeSpan Duration,
    string? SanitizedMessage = null
);

/// <summary>
/// Persistence contract for workflow definitions and operational run records.
/// </summary>
public interface IWorkflowRepository
{
    Task<WorkflowDefinition> CreateWorkflowAsync(WorkflowDefinition workflow, CancellationToken cancellationToken = default);
    Task<WorkflowDefinition?> GetWorkflowByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkflowDefinition>> GetAllWorkflowsAsync(CancellationToken cancellationToken = default);
    Task UpdateWorkflowAsync(WorkflowDefinition workflow, CancellationToken cancellationToken = default);
    Task DeleteWorkflowAsync(string id, CancellationToken cancellationToken = default);

    Task RecordRunAsync(WorkflowRunRecord runRecord, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkflowRunRecord>> GetRunHistoryAsync(string workflowId, int limit = 50, CancellationToken cancellationToken = default);
    Task<WorkflowRunRecord?> GetRunByIdAsync(string runId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Execution engine contract for workflows.
/// </summary>
public interface IWorkflowEngine
{
    Task<WorkflowRunRecord> ExecuteWorkflowAsync(string workflowId, IReadOnlyDictionary<string, string>? inputs = null, CancellationToken cancellationToken = default);
    Task<WorkflowRunRecord> ResumeWorkflowAsync(string runId, bool approved, CancellationToken cancellationToken = default);
    Task CancelWorkflowAsync(string runId, CancellationToken cancellationToken = default);
    event Action<WorkflowRunRecord>? RunStatusChanged;
}
