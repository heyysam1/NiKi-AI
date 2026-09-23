using System.ComponentModel;
using System.Windows;
using Microsoft.Extensions.Logging;
using NikiAI.Core.Lifecycle;
using NikiAI.Core.Security;

namespace NikiAI.App.Services;

/// <summary>
/// Implements the central application lifecycle coordinator.
/// Manages Windows startup registration, Desktop Pet Show/Hide independent of background runtime,
/// user-controlled close behavior (Minimize to tray vs Full exit), and clean shutdown.
/// </summary>
public class AppLifecycleManager : IAppLifecycleManager
{
    public const string StartWithWindowsKey = "Lifecycle.StartWithWindows";
    public const string CloseBehaviorKey = "Lifecycle.CloseBehavior";

    private readonly IStartupManager _startupManager;
    private readonly ISecureSettingsStore? _settingsStore;
    private readonly ILogger<AppLifecycleManager>? _logger;
    private CompanionWindow? _companionWindow;
    private AppCloseBehavior _closeBehavior = AppCloseBehavior.MinimizeToTray;
    private bool _startWithWindows;
    private readonly object _lock = new();

    public event EventHandler<bool>? PetVisibilityChanged;
    public event EventHandler<AppCloseBehavior>? CloseBehaviorChanged;
    public event EventHandler<bool>? StartWithWindowsChanged;

    public AppLifecycleManager(
        IStartupManager startupManager,
        ISecureSettingsStore? settingsStore = null,
        ILogger<AppLifecycleManager>? logger = null)
    {
        _startupManager = startupManager ?? throw new ArgumentNullException(nameof(startupManager));
        _settingsStore = settingsStore;
        _logger = logger;

        LoadSettings();
    }

    public void AttachCompanionWindow(CompanionWindow companionWindow)
    {
        _companionWindow = companionWindow;
        if (_companionWindow != null)
        {
            _companionWindow.IsVisibleChanged += (s, e) =>
            {
                PetVisibilityChanged?.Invoke(this, _companionWindow.IsVisible);
            };
        }
    }

    private void LoadSettings()
    {
        try
        {
            if (_settingsStore != null)
            {
                var startVal = Task.Run(async () => await _settingsStore.GetSecretAsync(StartWithWindowsKey).ConfigureAwait(false)).GetAwaiter().GetResult();
                if (bool.TryParse(startVal, out var startEnabled))
                {
                    _startWithWindows = startEnabled;
                }
                else
                {
                    _startWithWindows = _startupManager.IsStartupEnabled();
                }

                var closeVal = Task.Run(async () => await _settingsStore.GetSecretAsync(CloseBehaviorKey).ConfigureAwait(false)).GetAwaiter().GetResult();
                if (Enum.TryParse<AppCloseBehavior>(closeVal, out var behavior))
                {
                    _closeBehavior = behavior;
                }
            }
            else
            {
                _startWithWindows = _startupManager.IsStartupEnabled();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load lifecycle settings from secure store. Using defaults.");
            _startWithWindows = _startupManager.IsStartupEnabled();
        }
    }

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set
        {
            lock (_lock)
            {
                if (_startWithWindows != value)
                {
                    _startWithWindows = value;
                    try
                    {
                        _startupManager.SetStartupEnabled(value);
                        _settingsStore?.SetSecretAsync(StartWithWindowsKey, value.ToString());
                        _logger?.LogInformation("StartWithWindows updated to {Value}", value);
                        StartWithWindowsChanged?.Invoke(this, value);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Failed to update StartWithWindows setting to {Value}", value);
                        throw;
                    }
                }
            }
        }
    }

    public AppCloseBehavior CloseBehavior
    {
        get => _closeBehavior;
        set
        {
            lock (_lock)
            {
                if (_closeBehavior != value)
                {
                    _closeBehavior = value;
                    _settingsStore?.SetSecretAsync(CloseBehaviorKey, value.ToString());
                    _logger?.LogInformation("AppCloseBehavior updated to {Behavior}", value);
                    CloseBehaviorChanged?.Invoke(this, value);
                }
            }
        }
    }

    public bool IsPetVisible => _companionWindow?.IsVisible ?? false;

    public void ShowPet()
    {
        if (_companionWindow == null) return;

        _companionWindow.Dispatcher.Invoke(() =>
        {
            _companionWindow.Show();
            _companionWindow.WindowState = WindowState.Normal;
            _companionWindow.Activate();
            _logger?.LogInformation("Desktop Pet surface restored to visible.");
            PetVisibilityChanged?.Invoke(this, true);
        });
    }

    public void HidePet()
    {
        if (_companionWindow == null) return;

        _companionWindow.Dispatcher.Invoke(() =>
        {
            _companionWindow.Hide();
            _logger?.LogInformation("Desktop Pet surface hidden; background runtime remains active.");
            PetVisibilityChanged?.Invoke(this, false);
        });
    }

    public void TogglePet()
    {
        if (IsPetVisible)
        {
            HidePet();
        }
        else
        {
            ShowPet();
        }
    }

    public void HandleWindowClosing(object window, CancelEventArgs e)
    {
        if (CloseBehavior == AppCloseBehavior.MinimizeToTray)
        {
            _logger?.LogDebug("Window close intercepted: minimizing to tray per user setting.");
            e.Cancel = true;
            if (window is Window w)
            {
                w.Hide();
            }
        }
        else
        {
            _logger?.LogInformation("Window close intercepted: performing full application exit per user setting.");
            ExitApplication();
        }
    }

    public void ExitApplication()
    {
        _logger?.LogInformation("Explicit application exit requested. Performing full clean shutdown...");
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            System.Windows.Application.Current.Shutdown(0);
        });
    }
}
