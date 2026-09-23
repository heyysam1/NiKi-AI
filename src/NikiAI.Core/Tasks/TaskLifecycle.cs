namespace NikiAI.Core.Tasks;

/// <summary>
/// Task lifecycle states as specified in 02_FEATURES_AND_SCOPE.md and 04_ARCHITECTURE.md.
/// Authoritative single task state model across the application.
/// </summary>
public enum AgentTaskStatus
{
    Draft,
    Pending,
    Running,
    Waiting,
    NeedsApproval,
    Completed,
    Failed,
    Cancelled
}

/// <summary>
/// Task priority levels.
/// </summary>
public enum AgentTaskPriority
{
    Low,
    Normal,
    High,
    Critical
}

/// <summary>
/// Types of audit events recorded in task history.
/// </summary>
public enum TaskEventType
{
    Created,
    StatusChanged,
    ActionExecuted,
    ProgressReported,
    ApprovalRequested,
    ApprovalDecided,
    Completed,
    Failed,
    Cancelled,
    Archived
}

/// <summary>
/// Immutable audit event recording a discrete milestone or transition in a task's lifecycle.
/// </summary>
public record TaskEvent
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string TaskId { get; init; } = string.Empty;
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public TaskEventType EventType { get; init; } = TaskEventType.Created;
    public string Message { get; init; } = string.Empty;
    public string? DetailsJson { get; init; }

    public TaskEvent() { }

    public TaskEvent(string taskId, TaskEventType eventType, string message, string? detailsJson = null)
    {
        TaskId = taskId ?? throw new ArgumentNullException(nameof(taskId));
        EventType = eventType;
        Message = message ?? string.Empty;
        DetailsJson = detailsJson;
        Timestamp = DateTimeOffset.UtcNow;
    }
}

/// <summary>
/// Task state transition validator and state machine.
/// Enforces valid state transitions and rejects illegal transitions as mandated by the specification.
/// </summary>
public static class TaskStateMachine
{
    private static readonly Dictionary<AgentTaskStatus, HashSet<AgentTaskStatus>> ValidTransitions = new()
    {
        [AgentTaskStatus.Draft] = new() { AgentTaskStatus.Pending, AgentTaskStatus.Cancelled },
        [AgentTaskStatus.Pending] = new() { AgentTaskStatus.Running, AgentTaskStatus.Cancelled },
        [AgentTaskStatus.Running] = new() { AgentTaskStatus.Waiting, AgentTaskStatus.NeedsApproval, AgentTaskStatus.Completed, AgentTaskStatus.Failed, AgentTaskStatus.Cancelled },
        [AgentTaskStatus.Waiting] = new() { AgentTaskStatus.Running, AgentTaskStatus.Cancelled, AgentTaskStatus.Failed },
        [AgentTaskStatus.NeedsApproval] = new() { AgentTaskStatus.Running, AgentTaskStatus.Cancelled, AgentTaskStatus.Failed },
        [AgentTaskStatus.Completed] = new(), // Terminal state
        [AgentTaskStatus.Failed] = new(),    // Terminal state
        [AgentTaskStatus.Cancelled] = new()  // Terminal state
    };

    public static bool CanTransition(AgentTaskStatus current, AgentTaskStatus next)
    {
        if (ValidTransitions.TryGetValue(current, out var allowed))
        {
            return allowed.Contains(next);
        }
        return false;
    }

    public static void ValidateTransition(AgentTaskStatus current, AgentTaskStatus next)
    {
        if (!CanTransition(current, next))
        {
            throw new InvalidOperationException($"Illegal task state transition from '{current}' to '{next}'.");
        }
    }
}

/// <summary>
/// Core AgentTask domain model.
/// </summary>
public class AgentTask
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = string.Empty;
    public string NaturalLanguageRequest { get; set; } = string.Empty;
    public string StructuredGoal { get; set; } = string.Empty;
    public AgentTaskPriority Priority { get; set; } = AgentTaskPriority.Normal;
    public AgentTaskStatus Status { get; set; } = AgentTaskStatus.Draft;
    
    private int? _progressPercentage;
    /// <summary>
    /// Truthful measurable progress (0 to 100). Null if progress is unmeasurable.
    /// Never generates fake progress.
    /// </summary>
    public int? ProgressPercentage
    {
        get => _progressPercentage;
        set => _progressPercentage = value.HasValue ? Math.Clamp(value.Value, 0, 100) : null;
    }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? DueAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public List<string> ActionsTaken { get; set; } = new();
    public string? ResultSummary { get; set; }
    public string? ErrorInformation { get; set; }
    public List<string> ArtifactLinks { get; set; } = new();

    /// <summary>
    /// Soft-deletion flag preserving audit and historical integrity.
    /// </summary>
    public bool IsArchived { get; set; }

    public AgentTask() { }

    public AgentTask(string title, string naturalLanguageRequest, string? structuredGoal = null, AgentTaskPriority priority = AgentTaskPriority.Normal)
    {
        Title = title ?? string.Empty;
        NaturalLanguageRequest = naturalLanguageRequest ?? string.Empty;
        StructuredGoal = structuredGoal ?? NaturalLanguageRequest;
        Priority = priority;
        Status = AgentTaskStatus.Pending;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public void TransitionTo(AgentTaskStatus newStatus)
    {
        TaskStateMachine.ValidateTransition(Status, newStatus);
        Status = newStatus;

        if (newStatus == AgentTaskStatus.Running && StartedAt == null)
        {
            StartedAt = DateTimeOffset.UtcNow;
        }
        else if (newStatus is AgentTaskStatus.Completed or AgentTaskStatus.Failed or AgentTaskStatus.Cancelled)
        {
            CompletedAt = DateTimeOffset.UtcNow;
        }
    }
}

/// <summary>
/// Contract for task persistence, querying, and audit history.
/// </summary>
public interface ITaskRepository
{
    Task<AgentTask> CreateAsync(AgentTask task, CancellationToken cancellationToken = default);
    Task<AgentTask?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AgentTask>> GetAllAsync(bool includeArchived = false, int limit = 100, int offset = 0, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AgentTask>> GetByStatusAsync(AgentTaskStatus status, bool includeArchived = false, CancellationToken cancellationToken = default);
    Task UpdateAsync(AgentTask task, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically updates a task's status and writes the corresponding TaskEvent inside a single database transaction.
    /// </summary>
    Task TransitionStatusAsync(string taskId, AgentTaskStatus newStatus, string? message = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft-deletes a task by setting IsArchived = true, preserving full audit history.
    /// </summary>
    Task ArchiveAsync(string taskId, string? reason = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Destructive administrative permanent purge. Explicit and isolated.
    /// </summary>
    Task PurgePermanentlyAsync(string taskId, CancellationToken cancellationToken = default);

    Task AddEventAsync(TaskEvent taskEvent, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TaskEvent>> GetEventsAsync(string taskId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Event triggered when a task's status changes.
    /// Provides taskId, previousStatus, newStatus.
    /// </summary>
    event Action<string, AgentTaskStatus, AgentTaskStatus>? StatusChanged
    {
        add { }
        remove { }
    }
}
