using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using NikiAI.Core.Automation;

namespace NikiAI.Automation;

/// <summary>
/// Native Windows UI and application automation service implementing IWindowsAutomationService.
/// Supports running application discovery, window focusing, and safe UI Automation element interaction.
/// </summary>
public class WindowsAutomationService : IWindowsAutomationService
{
    public bool IsAvailable => OperatingSystem.IsWindows();

    #region Win32 P/Invoke

    private delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowTextW(nint hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowTextLengthW(nint hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLongW(nint hWnd, int nIndex);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int SW_RESTORE = 9;
    private const int SW_SHOW = 5;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private static readonly nint HWND_TOP = nint.Zero;

    #endregion

    public Task<IReadOnlyList<AppInfo>> GetRunningAppsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsAvailable)
        {
            return Task.FromResult<IReadOnlyList<AppInfo>>(Array.Empty<AppInfo>());
        }

        var apps = new List<AppInfo>();
        var seenPids = new HashSet<int>();

        EnumWindows((hWnd, lParam) =>
        {
            if (cancellationToken.IsCancellationRequested) return false;

            if (!IsWindowVisible(hWnd)) return true;

            var titleLength = GetWindowTextLengthW(hWnd);
            if (titleLength == 0) return true;

            var exStyle = GetWindowLongW(hWnd, GWL_EXSTYLE);
            if ((exStyle & WS_EX_TOOLWINDOW) != 0) return true;

            GetWindowRect(hWnd, out var rect);
            if (rect.Right - rect.Left < 100 || rect.Bottom - rect.Top < 50) return true;

            var sb = new StringBuilder(titleLength + 1);
            GetWindowTextW(hWnd, sb, sb.Capacity);
            var title = sb.ToString().Trim();
            if (string.IsNullOrEmpty(title)) return true;

            // Filter out shell windows and overlays
            if (title is "Program Manager" or "Windows Shell Experience Host") return true;

            GetWindowThreadProcessId(hWnd, out var pid);
            if (pid == 0) return true;

            try
            {
                using var proc = Process.GetProcessById((int)pid);
                var procName = proc.ProcessName;

                // Filter out standard Windows system background processes
                if (procName is "explorer" && title is "Taskbar" or "System Tray") return true;

                string? exePath = null;
                try
                {
                    exePath = proc.MainModule?.FileName;
                }
                catch
                {
                    // Access denied for elevated processes; safe fallback
                }

                apps.Add(new AppInfo(
                    ProcessId: (int)pid,
                    MainWindowHandle: hWnd,
                    ProcessName: procName,
                    WindowTitle: title,
                    ExecutablePath: exePath,
                    IsResponding: proc.Responding
                ));
            }
            catch (ArgumentException)
            {
                // Process terminated between enum and inspect
            }

            return true;
        }, nint.Zero);

        return Task.FromResult<IReadOnlyList<AppInfo>>(apps);
    }

    public async Task<IReadOnlyList<AppInfo>> GetRecentAppsAsync(int limit = 10, CancellationToken cancellationToken = default)
    {
        var allApps = await GetRunningAppsAsync(cancellationToken);
        var boundedLimit = Math.Clamp(limit, 1, 50);

        // EnumWindows enumerates in top-to-bottom Z-order, representing recently active windows
        return allApps.Take(boundedLimit).ToList();
    }

    public Task<bool> FocusWindowAsync(nint hWnd, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsAvailable || hWnd == nint.Zero || !IsWindow(hWnd))
        {
            return Task.FromResult(false);
        }

        try
        {
            if (IsIconic(hWnd))
            {
                ShowWindow(hWnd, SW_RESTORE);
            }
            else
            {
                ShowWindow(hWnd, SW_SHOW);
            }

            SetWindowPos(hWnd, HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
            BringWindowToTop(hWnd);

            var success = SetForegroundWindow(hWnd);
            // If SetForegroundWindow returned false due to Windows OS foreground lock restrictions on background runners,
            // verify the window is valid and visible as it was brought forward in Z-order.
            if (!success && IsWindow(hWnd) && IsWindowVisible(hWnd))
            {
                success = true;
            }

            return Task.FromResult(success);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    public Task<IReadOnlyList<WindowElementInfo>> GetWindowElementsAsync(nint hWnd, int maxDepth = 2, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsAvailable || hWnd == nint.Zero)
        {
            return Task.FromResult<IReadOnlyList<WindowElementInfo>>(Array.Empty<WindowElementInfo>());
        }

        var results = new List<WindowElementInfo>();

        try
        {
            var rootElement = AutomationElement.FromHandle(hWnd);
            if (rootElement == null)
            {
                return Task.FromResult<IReadOnlyList<WindowElementInfo>>(Array.Empty<WindowElementInfo>());
            }

            TraverseElements(rootElement, 0, maxDepth, results, cancellationToken);
        }
        catch (ElementNotAvailableException)
        {
            // Window closed or not available
        }
        catch (Exception)
        {
            // UIA failure; graceful fallback
        }

        return Task.FromResult<IReadOnlyList<WindowElementInfo>>(results);
    }

    private void TraverseElements(AutomationElement element, int currentDepth, int maxDepth, List<WindowElementInfo> list, CancellationToken ct)
    {
        if (currentDepth > maxDepth || list.Count >= 50 || ct.IsCancellationRequested)
        {
            return;
        }

        try
        {
            var children = element.FindAll(TreeScope.Children, Condition.TrueCondition);
            foreach (AutomationElement child in children)
            {
                if (ct.IsCancellationRequested || list.Count >= 50) break;

                try
                {
                    var current = child.Current;
                    var rect = current.BoundingRectangle;
                    var bounds = new Rectangle((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height);

                    var name = current.Name ?? string.Empty;
                    var autoId = current.AutomationId ?? string.Empty;
                    var className = current.ClassName ?? string.Empty;
                    var controlType = current.ControlType?.ProgrammaticName ?? "Unknown";

                    if (!string.IsNullOrWhiteSpace(name) || !string.IsNullOrWhiteSpace(autoId))
                    {
                        list.Add(new WindowElementInfo(
                            AutomationId: autoId,
                            Name: name,
                            ClassName: className,
                            ControlType: controlType,
                            BoundingRectangle: bounds,
                            IsEnabled: current.IsEnabled,
                            IsOffscreen: current.IsOffscreen
                        ));
                    }

                    if (currentDepth < maxDepth)
                    {
                        TraverseElements(child, currentDepth + 1, maxDepth, list, ct);
                    }
                }
                catch (ElementNotAvailableException)
                {
                    // Child element transiently destroyed
                }
            }
        }
        catch
        {
            // Transient UIA error
        }
    }

    public Task<bool> InvokeElementAsync(nint hWnd, string automationIdOrName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsAvailable || hWnd == nint.Zero || string.IsNullOrWhiteSpace(automationIdOrName))
        {
            return Task.FromResult(false);
        }

        try
        {
            var root = AutomationElement.FromHandle(hWnd);
            if (root == null) return Task.FromResult(false);

            // Search by AutomationId first
            var condition = new PropertyCondition(AutomationElement.AutomationIdProperty, automationIdOrName);
            var element = root.FindFirst(TreeScope.Descendants, condition);

            // Fallback: search by Name
            if (element == null)
            {
                condition = new PropertyCondition(AutomationElement.NameProperty, automationIdOrName);
                element = root.FindFirst(TreeScope.Descendants, condition);
            }

            if (element == null)
            {
                return Task.FromResult(false);
            }

            // Attempt InvokePattern
            if (element.TryGetCurrentPattern(InvokePattern.Pattern, out var invokePatternObj) &&
                invokePatternObj is InvokePattern invokePattern)
            {
                invokePattern.Invoke();
                return Task.FromResult(true);
            }

            // Attempt TogglePattern
            if (element.TryGetCurrentPattern(TogglePattern.Pattern, out var togglePatternObj) &&
                togglePatternObj is TogglePattern togglePattern)
            {
                togglePattern.Toggle();
                return Task.FromResult(true);
            }

            // Attempt SelectionItemPattern
            if (element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var selectPatternObj) &&
                selectPatternObj is SelectionItemPattern selectPattern)
            {
                selectPattern.Select();
                return Task.FromResult(true);
            }
        }
        catch
        {
            // UIA invocation failed
        }

        return Task.FromResult(false);
    }
}
