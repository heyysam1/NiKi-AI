using System.ComponentModel;

namespace NikiAI.Core.Lifecycle;

/// <summary>
/// Central manager for application lifecycle, startup registration, Desktop Pet visibility,
/// and close/background dispatching.
/// </summary>
public interface IAppLifecycleManager
{
    /// <summary>
    /// User-controlled ON/OFF setting for starting Niki AI with Windows.
    /// Setting this automatically persists and updates the startup registration.
    /// </summary>
    bool StartWithWindows { get; set; }

    /// <summary>
    /// User-controlled behavior when closing windows (MinimizeToTray vs ExitApplication).
    /// </summary>
    AppCloseBehavior CloseBehavior { get; set; }

    /// <summary>
    /// Whether the Desktop Pet window surface is currently visible.
    /// </summary>
    bool IsPetVisible { get; }

    /// <summary>
    /// Shows the Desktop Pet surface without restarting the application.
    /// </summary>
    void ShowPet();

    /// <summary>
    /// Hides the Desktop Pet surface without terminating the background assistant runtime.
    /// </summary>
    void HidePet();

    /// <summary>
    /// Toggles the Desktop Pet surface visibility.
    /// </summary>
    void TogglePet();

    /// <summary>
    /// Handles a window closing event according to the active CloseBehavior.
    /// If MinimizeToTray, cancels window destruction and hides to tray.
    /// If ExitApplication, initiates full clean exit.
    /// </summary>
    void HandleWindowClosing(object window, CancelEventArgs e);

    /// <summary>
    /// Initiates a full, clean shutdown of Niki AI, disposing all background services and windows.
    /// </summary>
    void ExitApplication();

    /// <summary>
    /// Fired when Desktop Pet visibility changes.
    /// </summary>
    event EventHandler<bool>? PetVisibilityChanged;

    /// <summary>
    /// Fired when the close behavior setting changes.
    /// </summary>
    event EventHandler<AppCloseBehavior>? CloseBehaviorChanged;

    /// <summary>
    /// Fired when the StartWithWindows setting changes.
    /// </summary>
    event EventHandler<bool>? StartWithWindowsChanged;
}
