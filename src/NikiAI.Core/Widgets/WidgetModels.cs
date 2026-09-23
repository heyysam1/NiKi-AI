namespace NikiAI.Core.Widgets;

/// <summary>
/// Preset size options for widgets as defined in 04_ARCHITECTURE.md and 03_DESIGN_SYSTEM.md.
/// </summary>
public enum WidgetSizeOption
{
    Standard,
    Compact,
    Wide
}

/// <summary>
/// Presentation state for widget cards (supports valid active, empty, and unavailable states).
/// </summary>
public enum WidgetPresentationState
{
    Active,
    Empty,
    Loading,
    Unavailable,
    Error
}

/// <summary>
/// Interface for the centralized widget refresh coordinator.
/// Manages cadence groups, event-driven pulses, and lifecycle suspension when the shelf is hidden.
/// Avoids independent polling loops per widget.
/// </summary>
public interface IWidgetRefreshCoordinator
{
    bool IsSuspended { get; }
    void Suspend();
    void Resume();
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync();
    Task RequestRefreshAsync(string widgetId, CancellationToken cancellationToken = default);
    Task RequestAllRefreshAsync(CancellationToken cancellationToken = default);
}
