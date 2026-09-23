using System.Drawing;

namespace NikiAI.Core.Automation;

/// <summary>
/// Information about a running desktop application window.
/// </summary>
public record AppInfo(
    int ProcessId,
    nint MainWindowHandle,
    string ProcessName,
    string WindowTitle,
    string? ExecutablePath = null,
    bool IsResponding = true
);

/// <summary>
/// Information about a UI Automation element within a window.
/// </summary>
public record WindowElementInfo(
    string AutomationId,
    string Name,
    string ClassName,
    string ControlType,
    Rectangle BoundingRectangle,
    bool IsEnabled,
    bool IsOffscreen
);

/// <summary>
/// High-level contract for Windows and UI automation operations.
/// </summary>
public interface IWindowsAutomationService
{
    bool IsAvailable { get; }

    Task<IReadOnlyList<AppInfo>> GetRunningAppsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AppInfo>> GetRecentAppsAsync(int limit = 10, CancellationToken cancellationToken = default);

    Task<bool> FocusWindowAsync(nint hWnd, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WindowElementInfo>> GetWindowElementsAsync(nint hWnd, int maxDepth = 2, CancellationToken cancellationToken = default);

    Task<bool> InvokeElementAsync(nint hWnd, string automationIdOrName, CancellationToken cancellationToken = default);
}
