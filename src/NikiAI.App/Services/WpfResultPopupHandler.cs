using System.Windows;
using Microsoft.Extensions.Logging;
using NikiAI.App.Views;
using NikiAI.Core.Notifications;
using NikiAI.Core.Scheduler;
using NikiAI.Core.Tasks;

namespace NikiAI.App.Services;

/// <summary>
/// WPF implementation of IResultPopupHandler presenting ResultPopupWindow on the UI thread.
/// </summary>
public class WpfResultPopupHandler : IResultPopupHandler
{
    private readonly IServiceProvider? _serviceProvider;
    private readonly ITaskRepository? _taskRepository;
    private readonly ILogger<WpfResultPopupHandler>? _logger;

    public WpfResultPopupHandler(
        IServiceProvider? serviceProvider = null,
        ITaskRepository? taskRepository = null,
        ILogger<WpfResultPopupHandler>? logger = null)
    {
        _serviceProvider = serviceProvider;
        _taskRepository = taskRepository;
        _logger = logger;
    }

    public Task ShowResultPopupAsync(NotificationPayload payload, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var app = System.Windows.Application.Current;
        if (app == null || app.Dispatcher.HasShutdownStarted || app.Dispatcher.HasShutdownFinished)
        {
            _logger?.LogWarning("Application or Dispatcher is shutting down; cannot display ResultPopupWindow.");
            return Task.CompletedTask;
        }

        app.Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                var popup = new ResultPopupWindow(
                    payload,
                    onViewResults: p => HandleViewResults(p),
                    onOpenArtifact: (p, artifact) => HandleOpenArtifact(artifact),
                    onSnooze: (p, duration) => HandleSnooze(p, duration),
                    onDismiss: p => _logger?.LogInformation("User dismissed popup for {Id}", p.NotificationId)
                );

                popup.Show();
                _logger?.LogInformation("ResultPopupWindow displayed for notification {Id} ('{Title}')", payload.NotificationId, payload.Title);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to display ResultPopupWindow for notification {Id}", payload.NotificationId);
            }
        }));

        return Task.CompletedTask;
    }

    private void HandleViewResults(NotificationPayload payload)
    {
        _logger?.LogInformation("User clicked 'View Results' for notification {Id} (Task: {TaskId})",
            payload.NotificationId, payload.AssociatedTaskId);

        if (!string.IsNullOrEmpty(payload.AssociatedTaskId) && _taskRepository != null)
        {
            try
            {
                var task = _taskRepository.GetByIdAsync(payload.AssociatedTaskId).GetAwaiter().GetResult();
                if (task != null)
                {
                    var detailWindow = new TaskDetailWindow(_taskRepository);
                    detailWindow.Show();
                    detailWindow.Activate();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to open TaskDetailWindow for task {TaskId}", payload.AssociatedTaskId);
            }
        }
    }

    private void HandleOpenArtifact(string artifactPath)
    {
        _logger?.LogInformation("User clicked 'Open Artifact': {Path}", artifactPath);
        if (!string.IsNullOrEmpty(artifactPath) && System.IO.File.Exists(artifactPath))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = artifactPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to open artifact at {Path}", artifactPath);
            }
        }
    }

    private void HandleSnooze(NotificationPayload payload, TimeSpan duration)
    {
        _logger?.LogInformation("User requested snooze for notification {Id} (Duration: {Duration})", payload.NotificationId, duration);
        var schedulerService = _serviceProvider?.GetService(typeof(ISchedulerService)) as ISchedulerService;
        if (schedulerService != null && !string.IsNullOrEmpty(payload.NotificationId))
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await schedulerService.SnoozeAsync(payload.NotificationId, duration);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning("Could not snooze item with notification ID {Id}: {Message}", payload.NotificationId, ex.Message);
                }
            });
        }
    }
}
