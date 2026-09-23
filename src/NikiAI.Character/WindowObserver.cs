using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using NikiAI.Core.Character;
using Point = System.Drawing.Point;

namespace NikiAI.Character;

/// <summary>
/// Observes native Windows top-level window geometry, active window changes, and display work areas.
/// Respects strict privacy boundaries: observes only window layout geometry, never window content.
/// </summary>
public class WindowObserver : IWindowObserver, IDisposable
{
    public event EventHandler? ActiveWindowChanged;
    public event EventHandler? DisplayTopologyChanged;

    private nint _lastActiveHwnd = nint.Zero;
    private bool _isDisposed;

    #region Win32 P/Invoke

    private delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint hWnd);

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

    private delegate bool MonitorEnumProc(nint hMonitor, nint hdcMonitor, ref RECT lprcMonitor, nint dwData);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(nint hdc, nint lprcClip, MonitorEnumProc lpfnEnum, nint dwData);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint hMonitor, ref MONITORINFO lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    #endregion

    public WindowObserver()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            }
            catch { }
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        DisplayTopologyChanged?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<WindowGeometry> GetTopLevelWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Array.Empty<WindowGeometry>();
        }

        var list = new List<WindowGeometry>();
        var zOrder = 0;
        var activeHwnd = GetForegroundWindow();

        EnumWindows((hWnd, lParam) =>
        {
            if (!IsWindowVisible(hWnd)) return true;

            var titleLength = GetWindowTextLengthW(hWnd);
            if (titleLength == 0) return true;

            var exStyle = GetWindowLongW(hWnd, GWL_EXSTYLE);
            if ((exStyle & WS_EX_TOOLWINDOW) != 0) return true;

            GetWindowRect(hWnd, out var r);
            var width = r.Right - r.Left;
            var height = r.Bottom - r.Top;
            if (width < 100 || height < 50) return true;

            var sb = new StringBuilder(titleLength + 1);
            GetWindowTextW(hWnd, sb, sb.Capacity);
            var title = sb.ToString().Trim();
            if (string.IsNullOrEmpty(title)) return true;

            if (title is "Program Manager" or "Windows Shell Experience Host") return true;

            GetWindowThreadProcessId(hWnd, out var pid);
            var procName = "unknown";
            if (pid != 0)
            {
                try
                {
                    using var proc = Process.GetProcessById((int)pid);
                    procName = proc.ProcessName;
                }
                catch { }
            }

            var isMin = IsIconic(hWnd);
            var bounds = new Rectangle(r.Left, r.Top, width, height);

            list.Add(new WindowGeometry(
                Handle: hWnd,
                Title: title,
                ProcessName: procName,
                Bounds: bounds,
                IsActive: hWnd == activeHwnd,
                IsMinimized: isMin,
                ZOrder: zOrder++
            ));

            return true;
        }, nint.Zero);

        return list;
    }

    public WindowGeometry? GetActiveWindow()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var hWnd = GetForegroundWindow();
        if (hWnd == nint.Zero || !IsWindowVisible(hWnd))
        {
            return null;
        }

        if (hWnd != _lastActiveHwnd)
        {
            _lastActiveHwnd = hWnd;
            ActiveWindowChanged?.Invoke(this, EventArgs.Empty);
        }

        var titleLength = GetWindowTextLengthW(hWnd);
        var title = string.Empty;
        if (titleLength > 0)
        {
            var sb = new StringBuilder(titleLength + 1);
            GetWindowTextW(hWnd, sb, sb.Capacity);
            title = sb.ToString().Trim();
        }

        GetWindowThreadProcessId(hWnd, out var pid);
        var procName = "unknown";
        if (pid != 0)
        {
            try
            {
                using var proc = Process.GetProcessById((int)pid);
                procName = proc.ProcessName;
            }
            catch { }
        }

        GetWindowRect(hWnd, out var r);
        var width = r.Right - r.Left;
        var height = r.Bottom - r.Top;
        var bounds = new Rectangle(r.Left, r.Top, width, height);

        return new WindowGeometry(
            Handle: hWnd,
            Title: title,
            ProcessName: procName,
            Bounds: bounds,
            IsActive: true,
            IsMinimized: IsIconic(hWnd),
            ZOrder: 0
        );
    }

    public IReadOnlyList<Rectangle> GetMonitorWorkAreas()
    {
        var areas = new List<Rectangle>();

        if (OperatingSystem.IsWindows())
        {
            try
            {
                EnumDisplayMonitors(nint.Zero, nint.Zero, (nint hMonitor, nint hdc, ref RECT rc, nint data) =>
                {
                    var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                    if (GetMonitorInfo(hMonitor, ref mi))
                    {
                        var width = mi.rcWork.Right - mi.rcWork.Left;
                        var height = mi.rcWork.Bottom - mi.rcWork.Top;
                        if (width > 0 && height > 0)
                        {
                            areas.Add(new Rectangle(mi.rcWork.Left, mi.rcWork.Top, width, height));
                        }
                    }
                    return true;
                }, nint.Zero);
            }
            catch { }
        }

        if (areas.Count == 0)
        {
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    // Retrieve primary screen work area from WPF SystemParameters
                    var wpfArea = SystemParameters.WorkArea;
                    areas.Add(new Rectangle((int)wpfArea.X, (int)wpfArea.Y, (int)wpfArea.Width, (int)wpfArea.Height));
                }
                catch
                {
                    areas.Add(new Rectangle(0, 0, 1920, 1040));
                }
            }
            else
            {
                areas.Add(new Rectangle(0, 0, 1920, 1040));
            }
        }

        return areas;
    }

    public Rectangle GetVirtualScreenBounds()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                return new Rectangle(
                    (int)SystemParameters.VirtualScreenLeft,
                    (int)SystemParameters.VirtualScreenTop,
                    (int)SystemParameters.VirtualScreenWidth,
                    (int)SystemParameters.VirtualScreenHeight
                );
            }
            catch
            {
                return new Rectangle(0, 0, 1920, 1080);
            }
        }

        return new Rectangle(0, 0, 1920, 1080);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        if (OperatingSystem.IsWindows())
        {
            try
            {
                SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            }
            catch { }
        }
    }
}
