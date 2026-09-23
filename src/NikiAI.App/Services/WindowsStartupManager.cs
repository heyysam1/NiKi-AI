using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using NikiAI.Core.Lifecycle;

namespace NikiAI.App.Services;

/// <summary>
/// Manages Windows startup registration using the Windows Registry (HKCU\Software\Microsoft\Windows\CurrentVersion\Run).
/// Supports custom registry subkeys for isolated automated testing.
/// </summary>
public class WindowsStartupManager : IStartupManager
{
    public const string DefaultRunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string DefaultAppName = "NikiAI";

    private readonly string _runKeyPath;
    private readonly string _appName;
    private readonly string _executablePath;
    private readonly ILogger<WindowsStartupManager>? _logger;

    public WindowsStartupManager(
        string? runKeyPath = null,
        string? appName = null,
        string? executablePath = null,
        ILogger<WindowsStartupManager>? logger = null)
    {
        _runKeyPath = runKeyPath ?? DefaultRunKeyPath;
        _appName = appName ?? DefaultAppName;
        _executablePath = executablePath ?? Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "NikiAI.App.exe");
        _logger = logger;
    }

    public bool IsStartupEnabled()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return false;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(_runKeyPath, writable: false);
            if (key == null) return false;

            var value = key.GetValue(_appName) as string;
            return !string.IsNullOrWhiteSpace(value);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to read Windows startup key from {Path}", _runKeyPath);
            return false;
        }
    }

    public void SetStartupEnabled(bool enabled)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            _logger?.LogWarning("Windows startup registration is only supported on Windows.");
            return;
        }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(_runKeyPath, writable: true);
            if (key == null)
            {
                throw new InvalidOperationException($"Cannot open or create registry subkey '{_runKeyPath}' for writing.");
            }

            if (enabled)
            {
                // Ensure executable path is quoted in case of spaces in directory path
                var command = $"\"{_executablePath}\"";
                key.SetValue(_appName, command, RegistryValueKind.String);
                _logger?.LogInformation("Registered Windows startup entry for {AppName} at {Path}", _appName, command);
            }
            else
            {
                if (key.GetValue(_appName) != null)
                {
                    key.DeleteValue(_appName, throwOnMissingValue: false);
                    _logger?.LogInformation("Removed Windows startup entry for {AppName}", _appName);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to update Windows startup registration ({Enabled})", enabled);
            throw;
        }
    }
}
