using Microsoft.Extensions.Logging;
using NikiAI.Core.Widgets;

namespace NikiAI.Widgets;

/// <summary>
/// Centralized coordinator for widget refreshes.
/// Manages grouped cadences (1s for clock/timer, 60s for system monitor) and event-driven updates.
/// Enforces complete suspension when the widget shelf is closed/hidden to eliminate background wakeups.
/// </summary>
public class WidgetRefreshCoordinator : IWidgetRefreshCoordinator, IDisposable
{
    private readonly WidgetRegistry _registry;
    private readonly ILogger<WidgetRefreshCoordinator>? _logger;
    private readonly object _lock = new();

    private System.Threading.Timer? _timer;
    private bool _isRunning;
    private bool _isSuspended;
    private long _tickCount;
    private bool _disposed;

    public bool IsSuspended
    {
        get
        {
            lock (_lock) return _isSuspended;
        }
    }

    public bool IsRunning
    {
        get
        {
            lock (_lock) return _isRunning;
        }
    }

    public WidgetRefreshCoordinator(WidgetRegistry registry, ILogger<WidgetRefreshCoordinator>? logger = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(WidgetRefreshCoordinator));
            if (_isRunning) return Task.CompletedTask;

            _isRunning = true;
            _isSuspended = false;
            _tickCount = 0;

            _timer = new System.Threading.Timer(OnTimerTick, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            _logger?.LogInformation("WidgetRefreshCoordinator started with centralized 1-second pulse.");
        }

        // Trigger immediate initial refresh for all visible widgets
        _ = RequestAllRefreshAsync(cancellationToken);
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        lock (_lock)
        {
            _isRunning = false;
            _timer?.Change(Timeout.Infinite, Timeout.Infinite);
            _timer?.Dispose();
            _timer = null;
            _logger?.LogInformation("WidgetRefreshCoordinator stopped.");
        }
        return Task.CompletedTask;
    }

    public void Suspend()
    {
        lock (_lock)
        {
            if (!_isRunning || _isSuspended) return;

            _isSuspended = true;
            _timer?.Change(Timeout.Infinite, Timeout.Infinite);
            _logger?.LogInformation("WidgetRefreshCoordinator suspended: all widget background pulses halted.");
        }
    }

    public void Resume()
    {
        lock (_lock)
        {
            if (!_isRunning || !_isSuspended) return;

            _isSuspended = false;
            _timer?.Change(TimeSpan.Zero, TimeSpan.FromSeconds(1));
            _logger?.LogInformation("WidgetRefreshCoordinator resumed: central pulses restored.");
        }

        _ = RequestAllRefreshAsync();
    }

    public async Task RequestRefreshAsync(string widgetId, CancellationToken cancellationToken = default)
    {
        var widget = _registry.GetWidget(widgetId);
        if (widget != null && widget.IsVisible)
        {
            try
            {
                await widget.RefreshAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed refreshing widget '{Id}'", widgetId);
            }
        }
    }

    public async Task RequestAllRefreshAsync(CancellationToken cancellationToken = default)
    {
        var widgets = _registry.GetAllWidgets().Where(w => w.IsVisible).ToList();
        foreach (var widget in widgets)
        {
            if (cancellationToken.IsCancellationRequested) break;
            try
            {
                await widget.RefreshAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed refreshing widget '{Id}' during batch refresh", widget.Id);
            }
        }
    }

    private void OnTimerTick(object? state)
    {
        lock (_lock)
        {
            if (!_isRunning || _isSuspended) return;
            _tickCount++;
        }

        var currentTick = Interlocked.Read(ref _tickCount);

        // 1-Second Cadence: Clock and Focus Timer (if running)
        _ = RequestRefreshAsync("clock");
        _ = RequestRefreshAsync("focus_timer");

        // 60-Second Cadence: System Monitor
        if (currentTick % 60 == 0)
        {
            _ = RequestRefreshAsync("system_monitor");
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _isRunning = false;
            _timer?.Dispose();
            _timer = null;
        }
    }
}
