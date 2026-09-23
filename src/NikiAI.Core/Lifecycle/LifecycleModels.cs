namespace NikiAI.Core.Lifecycle;

/// <summary>
/// User-configurable behavior when the main application window or companion is closed.
/// </summary>
public enum AppCloseBehavior
{
    /// <summary>
    /// Closing hides the window to the system tray while keeping all background services running.
    /// </summary>
    MinimizeToTray,

    /// <summary>
    /// Closing immediately initiates a full clean application shutdown.
    /// </summary>
    ExitApplication
}

/// <summary>
/// Represents the current status of Windows startup registration.
/// </summary>
public record StartupStatus(bool IsEnabled, string ExecutablePath, string? ErrorMessage = null);

/// <summary>
/// High-level runtime lifecycle states for the Niki AI application.
/// </summary>
public enum LifecycleState
{
    Starting,
    Running,
    BackgroundOnly,
    Exiting
}
