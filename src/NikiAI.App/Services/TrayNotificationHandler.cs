using Microsoft.Extensions.Logging;
using NikiAI.Core.Notifications;

namespace NikiAI.App.Services;

/// <summary>
/// Native notification handler delivering Windows notifications via the system tray NotifyIcon.
/// </summary>
public class TrayNotificationHandler : INativeNotificationHandler
{
    private readonly SystemTrayManager? _trayManager;
    private readonly ILogger<TrayNotificationHandler>? _logger;

    public TrayNotificationHandler(SystemTrayManager? trayManager = null, ILogger<TrayNotificationHandler>? logger = null)
    {
        _trayManager = trayManager;
        _logger = logger;
    }

    public Task ShowNativeNotificationAsync(NotificationPayload payload, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var toolTipIcon = payload.CompletionState switch
        {
            TaskCompletionState.Failed => System.Windows.Forms.ToolTipIcon.Error,
            TaskCompletionState.Reminder => System.Windows.Forms.ToolTipIcon.Info,
            TaskCompletionState.Success => System.Windows.Forms.ToolTipIcon.Info,
            _ => System.Windows.Forms.ToolTipIcon.Info
        };

        _trayManager?.ShowBalloonTip(payload.Title, payload.SummaryMessage, 5000, toolTipIcon);
        _logger?.LogInformation("Native tray notification dispatched for '{Title}'", payload.Title);
        return Task.CompletedTask;
    }
}
