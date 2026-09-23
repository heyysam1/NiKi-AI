using System.Diagnostics;
using Microsoft.Extensions.Logging;
using NikiAI.Core.Tools;

namespace NikiAI.App.Services;

/// <summary>
/// Safe Windows process launcher for approved application catalog entries.
/// Does not accept arbitrary executable paths or execute shell command strings.
/// </summary>
public class WindowsProcessLauncher : IProcessLauncher
{
    private readonly ILogger<WindowsProcessLauncher>? _logger;

    public WindowsProcessLauncher(ILogger<WindowsProcessLauncher>? logger = null)
    {
        _logger = logger;
    }

    public Task<ProcessLaunchResult> LaunchApprovedAppAsync(
        ApprovedAppEntry app,
        string? arguments = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(app);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = app.ExecutableName,
                Arguments = arguments ?? string.Empty,
                UseShellExecute = true,
                CreateNoWindow = false
            };

            _logger?.LogInformation("Launching approved application '{AppName}' ({ExecutableName})...", app.DisplayName, app.ExecutableName);

            var process = Process.Start(startInfo);
            if (process == null)
            {
                return Task.FromResult(ProcessLaunchResult.Failed(app.DisplayName, "System failed to start the process."));
            }

            return Task.FromResult(ProcessLaunchResult.Successful(app.DisplayName, process.Id));
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to launch approved app '{AppName}'.", app.DisplayName);
            return Task.FromResult(ProcessLaunchResult.Failed(app.DisplayName, ex.Message));
        }
    }
}
