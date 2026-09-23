using Microsoft.Extensions.Logging;
using NikiAI.Core.Character;
using NikiAI.Core.Notifications;
using NikiAI.Core.Tasks;

namespace NikiAI.Memory;

/// <summary>
/// Ephemeral, read-only provider of non-sensitive runtime contextual signals for the Desktop Pet.
/// Serves as a foundation for future behavior directors without executing autonomous loops or AI planning.
/// Operates independently of whether permanent memory is enabled or disabled.
/// </summary>
public class DesktopPetContextProvider : IPetContextProvider
{
    private readonly ITaskRepository? _taskRepository;
    private readonly INotificationRepository? _notificationRepository;
    private readonly ILogger<DesktopPetContextProvider>? _logger;

    private readonly DateTimeOffset _sessionStartTime;
    private DateTimeOffset? _lastPetInteractionTime;

    public DesktopPetContextProvider(
        ITaskRepository? taskRepository = null,
        INotificationRepository? notificationRepository = null,
        ILogger<DesktopPetContextProvider>? logger = null)
    {
        _taskRepository = taskRepository;
        _notificationRepository = notificationRepository;
        _logger = logger;
        _sessionStartTime = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Records user interaction with the pet (such as clicks, drags, or petting).
    /// Kept only in-memory; never written to persistent memory.
    /// </summary>
    public void RecordUserInteraction()
    {
        _lastPetInteractionTime = DateTimeOffset.UtcNow;
        _logger?.LogDebug("Pet interaction recorded at {Timestamp}", _lastPetInteractionTime);
    }

    /// <summary>
    /// Produces a read-only snapshot of current non-sensitive application and environment state.
    /// Does not inspect window contents, notification text, or perform screen capture.
    /// </summary>
    public PetContextSnapshot GetContextSnapshot()
    {
        var now = DateTimeOffset.UtcNow;
        var sessionDuration = now - _sessionStartTime;

        // 1. Derive task status from active tasks (non-sensitive state)
        AgentTaskStatus? taskState = null;
        if (_taskRepository != null)
        {
            try
            {
                var running = _taskRepository.GetByStatusAsync(AgentTaskStatus.Running).GetAwaiter().GetResult();
                var approval = _taskRepository.GetByStatusAsync(AgentTaskStatus.NeedsApproval).GetAwaiter().GetResult();
                var waiting = _taskRepository.GetByStatusAsync(AgentTaskStatus.Waiting).GetAwaiter().GetResult();
                var primaryActive = running.FirstOrDefault() ?? approval.FirstOrDefault() ?? waiting.FirstOrDefault();
                taskState = primaryActive?.Status;
            }
            catch (Exception ex)
            {
                _logger?.LogTrace(ex, "Failed to query active tasks for pet context.");
            }
        }

        // 2. Count notifications without reading sensitive content
        int unreadCount = 0;
        if (_notificationRepository != null)
        {
            try
            {
                var history = _notificationRepository.GetHistoryAsync(10).GetAwaiter().GetResult();
                unreadCount = history.Count;
            }
            catch (Exception ex)
            {
                _logger?.LogTrace(ex, "Failed to query unread notifications for pet context.");
            }
        }

        // 3. User active state based on recent interaction within 5 minutes
        bool isUserActive = _lastPetInteractionTime.HasValue && (now - _lastPetInteractionTime.Value) < TimeSpan.FromMinutes(5);

        // 4. Time of day bucket
        var localHour = DateTime.Now.Hour;
        var timeOfDay = localHour switch
        {
            >= 5 and < 12 => TimeOfDayBucket.Morning,
            >= 12 and < 17 => TimeOfDayBucket.Afternoon,
            >= 17 and < 22 => TimeOfDayBucket.Evening,
            _ => TimeOfDayBucket.Night
        };

        return new PetContextSnapshot(
            CurrentTaskState: taskState,
            ActiveNotificationCount: unreadCount,
            IsUserActive: isUserActive,
            SessionDuration: sessionDuration,
            TimeOfDay: timeOfDay,
            LastPetInteractionTime: _lastPetInteractionTime
        );
    }
}
