using Microsoft.Extensions.Logging;
using NikiAI.Core.Lifecycle;
using NikiAI.Core.Tasks;

namespace NikiAI.Workflows;

/// <summary>
/// Broadcast hub adapting task and workflow lifecycle events for UI listeners without managing task transitions.
/// Strictly enforces privacy: signals contain ONLY SignalType, opaque GUID SourceId, and Timestamp.
/// </summary>
public class TaskLifecycleSignalHub : ITaskLifecycleSignalHub
{
    private readonly ILogger<TaskLifecycleSignalHub>? _logger;
    private readonly object _lock = new();

    public event Action<TaskLifecycleSignal>? SignalEmitted;

    public TaskLifecycleSignalHub(ILogger<TaskLifecycleSignalHub>? logger = null)
    {
        _logger = logger;
    }

    public void AttachTaskRepository(ITaskRepository taskRepository)
    {
        ArgumentNullException.ThrowIfNull(taskRepository);
        taskRepository.StatusChanged += OnTaskStatusChanged;
    }

    private void OnTaskStatusChanged(string taskId, AgentTaskStatus previousStatus, AgentTaskStatus newStatus)
    {
        // Parse taskId as Guid, or generate deterministic/opaque Guid so no plain string titles leak
        var sourceId = Guid.TryParse(taskId, out var g) ? g : Guid.NewGuid();

        switch (newStatus)
        {
            case AgentTaskStatus.Running:
                EmitSignal(TaskLifecycleSignalType.TaskStarted, sourceId);
                break;
            case AgentTaskStatus.Waiting:
                EmitSignal(TaskLifecycleSignalType.TaskWaiting, sourceId);
                break;
            case AgentTaskStatus.NeedsApproval:
                EmitSignal(TaskLifecycleSignalType.ApprovalRequired, sourceId);
                break;
            case AgentTaskStatus.Completed:
                EmitSignal(TaskLifecycleSignalType.TaskCompleted, sourceId);
                break;
            case AgentTaskStatus.Failed:
            case AgentTaskStatus.Cancelled:
                EmitSignal(TaskLifecycleSignalType.TaskFailed, sourceId);
                break;
        }
    }

    public void EmitSignal(TaskLifecycleSignal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);

        _logger?.LogDebug("Lifecycle signal emitted: Type={Type}, SourceId={SourceId}, Timestamp={Timestamp}",
            signal.SignalType, signal.SourceId, signal.Timestamp);

        SignalEmitted?.Invoke(signal);
    }

    public void EmitSignal(TaskLifecycleSignalType signalType, Guid sourceId)
    {
        var signal = new TaskLifecycleSignal(signalType, sourceId, DateTimeOffset.UtcNow);
        EmitSignal(signal);
    }
}
