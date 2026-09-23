namespace NikiAI.Core.Lifecycle;

/// <summary>
/// Discrete event types for task and workflow lifecycle transitions.
/// </summary>
public enum TaskLifecycleSignalType
{
    TaskStarted = 0,
    TaskWaiting = 1,
    ApprovalRequired = 2,
    TaskCompleted = 3,
    TaskFailed = 4,
    WorkflowNotification = 5
}

/// <summary>
/// Strictly non-sensitive lifecycle signal emitted to Character Runtime and UI listeners.
/// Contains ONLY SignalType, opaque GUID SourceId, and Timestamp.
/// Excludes task titles, prompts, workflow payloads, clipboard contents, secrets, and notification text.
/// </summary>
public record TaskLifecycleSignal(
    TaskLifecycleSignalType SignalType,
    Guid SourceId,
    DateTimeOffset Timestamp
);

/// <summary>
/// Lightweight broadcast hub adapting task and workflow lifecycle events for UI listeners without managing task transitions.
/// </summary>
public interface ITaskLifecycleSignalHub
{
    void EmitSignal(TaskLifecycleSignal signal);
    void EmitSignal(TaskLifecycleSignalType signalType, Guid sourceId);
    event Action<TaskLifecycleSignal>? SignalEmitted;
}
