using NikiAI.Core.Notifications;
using NikiAI.Core.Widgets;

namespace NikiAI.Widgets;

/// <summary>
/// Focus Timer Widget: Countdown timer for focused work sessions.
/// Relies strictly on the centralized WidgetRefreshCoordinator 1-second pulse.
/// Does NOT create independent polling timers or background loops.
/// Fires notification via INotificationService upon session completion.
/// </summary>
public class FocusTimerWidget : BaseWidget
{
    private const int DefaultFocusSeconds = 25 * 60; // 25 minutes
    private readonly INotificationService? _notificationService;

    private int _remainingSeconds = DefaultFocusSeconds;
    private bool _isRunning;
    private int _completedCycles;

    public override string Id => "focus_timer";
    public override string Title => "Focus Timer";
    public override WidgetCategory Category => WidgetCategory.Productivity;
    public override string IconGlyph => "⏱";

    public bool IsTimerRunning => _isRunning;
    public int RemainingSeconds => _remainingSeconds;

    public FocusTimerWidget(INotificationService? notificationService = null)
    {
        _notificationService = notificationService;
        ActionLabel = "Start";
        UpdateDisplay();
    }

    public override async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_isRunning)
        {
            if (_remainingSeconds > 0)
            {
                _remainingSeconds--;
                UpdateDisplay();
            }

            if (_remainingSeconds <= 0)
            {
                _isRunning = false;
                _completedCycles++;
                _remainingSeconds = DefaultFocusSeconds;
                ActionLabel = "Start";
                UpdateDisplay();

                if (_notificationService != null)
                {
                    try
                    {
                        var payload = new NotificationPayload(
                            notificationId: Guid.NewGuid().ToString("N"),
                            title: "Focus Session Completed!",
                            summaryMessage: $"Great job! Focus session #{_completedCycles} finished. Take a short 5-minute break.",
                            type: NotificationType.Reminder,
                            completionState: TaskCompletionState.Success
                        );
                        await _notificationService.ShowNotificationAsync(payload, cancellationToken).ConfigureAwait(false);
                    }
                    catch
                    {
                        // Non-critical notification failure
                    }
                }
            }
        }
    }

    public override Task ExecuteActionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _isRunning = !_isRunning;
        ActionLabel = _isRunning ? "Pause" : "Resume";
        UpdateDisplay();

        return Task.CompletedTask;
    }

    public void Reset()
    {
        _isRunning = false;
        _remainingSeconds = DefaultFocusSeconds;
        ActionLabel = "Start";
        UpdateDisplay();
    }

    private void UpdateDisplay()
    {
        var minutes = _remainingSeconds / 60;
        var seconds = _remainingSeconds % 60;
        PrimaryDisplayValue = $"{minutes:D2}:{seconds:D2}";
        SecondaryDisplayValue = _isRunning
            ? $"Focusing (Session #{_completedCycles + 1})"
            : $"Ready (Completed: {_completedCycles})";

        PresentationState = _isRunning
            ? WidgetPresentationState.Active
            : WidgetPresentationState.Empty;
    }
}
