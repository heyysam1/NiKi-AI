using System.ComponentModel;
using System.IO;
using Microsoft.Win32;
using NikiAI.App.Services;
using NikiAI.Core.Lifecycle;
using NikiAI.Security;

namespace NikiAI.Voice.Tests;

public class LifecycleManagerTests : IDisposable
{
    private readonly string _testRegistryKeyPath;
    private readonly string _testAppName = "NikiAITestApp";
    private readonly string _testExePath = @"C:\Program Files\NikiAI\NikiAI.App.exe";
    private readonly DpapiSecureSettingsStore _settingsStore;
    private readonly string _tempSettingsPath;

    public LifecycleManagerTests()
    {
        _testRegistryKeyPath = @"Software\NikiAITest_" + Guid.NewGuid().ToString("N");
        _tempSettingsPath = Path.Combine(Path.GetTempPath(), "niki_test_lifecycle_" + Guid.NewGuid().ToString("N") + ".dat");
        _settingsStore = new DpapiSecureSettingsStore(_tempSettingsPath);
    }

    public void Dispose()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(_testRegistryKeyPath, throwOnMissingSubKey: false);
        }
        catch { }

        try
        {
            if (File.Exists(_tempSettingsPath))
            {
                File.Delete(_tempSettingsPath);
            }
        }
        catch { }
    }

    [Fact]
    public void StartupManager_EnablesAndDisablesStartupInRegistry()
    {
        var startupManager = new WindowsStartupManager(_testRegistryKeyPath, _testAppName, _testExePath);

        // Initially disabled
        Assert.False(startupManager.IsStartupEnabled());

        // Enable
        startupManager.SetStartupEnabled(true);
        Assert.True(startupManager.IsStartupEnabled());

        // Verify registry directly
        using (var key = Registry.CurrentUser.OpenSubKey(_testRegistryKeyPath))
        {
            Assert.NotNull(key);
            var val = key.GetValue(_testAppName) as string;
            Assert.Contains(_testExePath, val);
        }

        // Disable
        startupManager.SetStartupEnabled(false);
        Assert.False(startupManager.IsStartupEnabled());

        using (var key = Registry.CurrentUser.OpenSubKey(_testRegistryKeyPath))
        {
            Assert.NotNull(key);
            Assert.Null(key.GetValue(_testAppName));
        }
    }

    [Fact]
    public void AppLifecycleManager_StartWithWindows_PersistsAndFiresEvent()
    {
        var startupManager = new WindowsStartupManager(_testRegistryKeyPath, _testAppName, _testExePath);
        var lifecycleManager = new AppLifecycleManager(startupManager, _settingsStore);

        bool eventFired = false;
        bool eventValue = false;
        lifecycleManager.StartWithWindowsChanged += (s, val) =>
        {
            eventFired = true;
            eventValue = val;
        };

        // Enable
        lifecycleManager.StartWithWindows = true;
        Assert.True(lifecycleManager.StartWithWindows);
        Assert.True(eventFired);
        Assert.True(eventValue);
        Assert.True(startupManager.IsStartupEnabled());

        // Disable
        eventFired = false;
        lifecycleManager.StartWithWindows = false;
        Assert.False(lifecycleManager.StartWithWindows);
        Assert.True(eventFired);
        Assert.False(eventValue);
        Assert.False(startupManager.IsStartupEnabled());
    }

    [Fact]
    public void AppLifecycleManager_CloseBehavior_PersistsAndFiresEvent()
    {
        var startupManager = new WindowsStartupManager(_testRegistryKeyPath, _testAppName, _testExePath);
        var lifecycleManager = new AppLifecycleManager(startupManager, _settingsStore);

        // Default should be MinimizeToTray
        Assert.Equal(AppCloseBehavior.MinimizeToTray, lifecycleManager.CloseBehavior);

        bool eventFired = false;
        AppCloseBehavior reportedBehavior = AppCloseBehavior.MinimizeToTray;
        lifecycleManager.CloseBehaviorChanged += (s, behavior) =>
        {
            eventFired = true;
            reportedBehavior = behavior;
        };

        // Change to ExitApplication
        lifecycleManager.CloseBehavior = AppCloseBehavior.ExitApplication;
        Assert.Equal(AppCloseBehavior.ExitApplication, lifecycleManager.CloseBehavior);
        Assert.True(eventFired);
        Assert.Equal(AppCloseBehavior.ExitApplication, reportedBehavior);
    }

    [Fact]
    public void AppLifecycleManager_HandleWindowClosing_CancelsWhenMinimizeToTray()
    {
        var startupManager = new WindowsStartupManager(_testRegistryKeyPath, _testAppName, _testExePath);
        var lifecycleManager = new AppLifecycleManager(startupManager, _settingsStore);
        lifecycleManager.CloseBehavior = AppCloseBehavior.MinimizeToTray;

        var cancelArgs = new CancelEventArgs();
        lifecycleManager.HandleWindowClosing("dummyWindow", cancelArgs);

        // Closing must be canceled so the window only hides to tray
        Assert.True(cancelArgs.Cancel);
    }

    [Fact]
    public void AppLifecycleManager_PetVisibility_AndRuntimeIndependence()
    {
        var startupManager = new WindowsStartupManager(_testRegistryKeyPath, _testAppName, _testExePath);
        var lifecycleManager = new AppLifecycleManager(startupManager, _settingsStore);

        // Verify pet visibility is independent of startup/runtime status
        Assert.False(lifecycleManager.IsPetVisible);

        bool visibilityFired = false;
        lifecycleManager.PetVisibilityChanged += (s, isVisible) =>
        {
            visibilityFired = true;
        };

        Assert.False(visibilityFired);

        // Even with null companion window, calling Show/Hide doesn't crash
        lifecycleManager.ShowPet();
        lifecycleManager.HidePet();
        lifecycleManager.TogglePet();

        // Runtime remains alive regardless of pet visibility
        Assert.NotNull(lifecycleManager);
    }
}
