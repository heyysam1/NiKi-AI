using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using NikiAI.Core.Character;
using NikiAI.Core.Vision;

namespace NikiAI.Automation;

/// <summary>
/// Native Windows screen and window capture service implementing IScreenCaptureService.
/// Reuses Phase 9 geometry abstractions (IWindowObserver, WindowGeometry, Rectangle).
/// Strictly executes on-demand, enforces user privacy gates, and holds raw pixels only in ephemeral memory.
/// </summary>
public class ScreenCaptureService : IScreenCaptureService
{
    private readonly IWindowObserver? _windowObserver;
    private readonly ILogger<ScreenCaptureService>? _logger;
    private int _captureCount;

    public bool IsAvailable => OperatingSystem.IsWindows();
    public bool ScreenAwarenessEnabled { get; set; } = true;
    public int CaptureCount => Volatile.Read(ref _captureCount);

    #region Win32 P/Invoke Fallback

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint hWnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    #endregion

    public ScreenCaptureService(
        IWindowObserver? windowObserver = null,
        ILogger<ScreenCaptureService>? logger = null)
    {
        _windowObserver = windowObserver;
        _logger = logger;
    }

    public Task<ScreenCaptureResult> CapturePrimaryScreenAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!ScreenAwarenessEnabled)
        {
            _logger?.LogWarning("Screen capture rejected: Screen awareness is disabled in privacy settings.");
            return Task.FromResult(ScreenCaptureResult.Failed("Screen awareness is disabled in privacy settings.", "PrimaryScreen"));
        }

        if (!IsAvailable)
        {
            return Task.FromResult(ScreenCaptureResult.Failed("Screen capture is only supported on Windows operating systems.", "PrimaryScreen"));
        }

        try
        {
            Rectangle bounds;
            if (_windowObserver != null)
            {
                var workAreas = _windowObserver.GetMonitorWorkAreas();
                bounds = workAreas.Count > 0 ? workAreas[0] : new Rectangle(0, 0, 1920, 1080);
            }
            else
            {
                // Fallback using Windows virtual screen metrics
                bounds = new Rectangle(0, 0, 1920, 1080);
            }

            var result = CaptureRegionInternal(bounds, "PrimaryScreen", cancellationToken);
            return Task.FromResult(result);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to capture primary screen.");
            return Task.FromResult(ScreenCaptureResult.Failed($"Primary screen capture failed: {ex.Message}", "PrimaryScreen"));
        }
    }

    public Task<ScreenCaptureResult> CaptureActiveWindowAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!ScreenAwarenessEnabled)
        {
            _logger?.LogWarning("Active window capture rejected: Screen awareness is disabled in privacy settings.");
            return Task.FromResult(ScreenCaptureResult.Failed("Screen awareness is disabled in privacy settings.", "ActiveWindow"));
        }

        if (!IsAvailable)
        {
            return Task.FromResult(ScreenCaptureResult.Failed("Screen capture is only supported on Windows operating systems.", "ActiveWindow"));
        }

        try
        {
            nint activeHwnd = nint.Zero;
            Rectangle windowBounds = Rectangle.Empty;
            string windowTitle = "Active Window";

            if (_windowObserver != null)
            {
                var activeWin = _windowObserver.GetActiveWindow();
                if (activeWin != null)
                {
                    activeHwnd = activeWin.Handle;
                    windowBounds = activeWin.Bounds;
                    windowTitle = activeWin.Title;
                }
            }

            if (activeHwnd == nint.Zero)
            {
                activeHwnd = GetForegroundWindow();
                if (activeHwnd != nint.Zero && GetWindowRect(activeHwnd, out var rect))
                {
                    windowBounds = new Rectangle(rect.Left, rect.Top, Math.Max(1, rect.Right - rect.Left), Math.Max(1, rect.Bottom - rect.Top));
                }
            }

            if (activeHwnd == nint.Zero || windowBounds.Width <= 0 || windowBounds.Height <= 0)
            {
                // If no active foreground window found, capture primary screen as safe fallback
                return CapturePrimaryScreenAsync(cancellationToken);
            }

            var sourceDesc = $"Window: {windowTitle} (HWND: 0x{activeHwnd:X})";
            var result = CaptureRegionInternal(windowBounds, sourceDesc, cancellationToken);
            return Task.FromResult(result);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to capture active window.");
            return Task.FromResult(ScreenCaptureResult.Failed($"Active window capture failed: {ex.Message}", "ActiveWindow"));
        }
    }

    public Task<ScreenCaptureResult> CaptureWindowAsync(nint hWnd, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!ScreenAwarenessEnabled)
        {
            return Task.FromResult(ScreenCaptureResult.Failed("Screen awareness is disabled in privacy settings.", $"HWND: 0x{hWnd:X}"));
        }

        if (!IsAvailable || hWnd == nint.Zero || !IsWindow(hWnd))
        {
            return Task.FromResult(ScreenCaptureResult.Failed("Invalid or unavailable window handle.", $"HWND: 0x{hWnd:X}"));
        }

        try
        {
            if (!GetWindowRect(hWnd, out var rect))
            {
                return Task.FromResult(ScreenCaptureResult.Failed("Failed to obtain window bounds.", $"HWND: 0x{hWnd:X}"));
            }

            var bounds = new Rectangle(rect.Left, rect.Top, Math.Max(1, rect.Right - rect.Left), Math.Max(1, rect.Bottom - rect.Top));
            var result = CaptureRegionInternal(bounds, $"HWND: 0x{hWnd:X}", cancellationToken);
            return Task.FromResult(result);
        }
        catch (Exception ex)
        {
            return Task.FromResult(ScreenCaptureResult.Failed($"Window capture failed: {ex.Message}", $"HWND: 0x{hWnd:X}"));
        }
    }

    public Task<ScreenCaptureResult> CaptureRegionAsync(Rectangle bounds, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!ScreenAwarenessEnabled)
        {
            return Task.FromResult(ScreenCaptureResult.Failed("Screen awareness is disabled in privacy settings.", "CustomRegion"));
        }

        if (!IsAvailable)
        {
            return Task.FromResult(ScreenCaptureResult.Failed("Screen capture is only supported on Windows operating systems.", "CustomRegion"));
        }

        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return Task.FromResult(ScreenCaptureResult.Failed("Invalid capture region dimensions.", "CustomRegion"));
        }

        var result = CaptureRegionInternal(bounds, $"Region: {bounds}", cancellationToken);
        return Task.FromResult(result);
    }

    private ScreenCaptureResult CaptureRegionInternal(Rectangle bounds, string source, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Enforce safe minimum and maximum dimension bounds
        int width = Math.Clamp(bounds.Width, 1, 7680);
        int height = Math.Clamp(bounds.Height, 1, 4320);

        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);

        bool isSyntheticFallback = false;
        try
        {
            graphics.CopyFromScreen(
                bounds.Left,
                bounds.Top,
                0,
                0,
                new Size(width, height),
                CopyPixelOperation.SourceCopy);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 6)
        {
            isSyntheticFallback = true;
            // In headless, non-interactive Windows service, or virtual desktop sessions
            // where the interactive GDI display device context (winsta0) is not accessible,
            // generate a clean synthetic desktop capture frame.
            _logger?.LogWarning(ex, "Display DC is inaccessible in non-interactive session (Win32 Error 6). Generating synthetic desktop capture frame.");
            graphics.Clear(Color.FromArgb(240, 240, 245));
            using var font = new Font(FontFamily.GenericSansSerif, 12, FontStyle.Regular);
            using var brush = new SolidBrush(Color.FromArgb(50, 50, 60));
            graphics.DrawString($"Niki AI Desktop Frame ({width}x{height})", font, brush, 10, 10);
        }

        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);
        var bytes = ms.ToArray();

        Interlocked.Increment(ref _captureCount);
        _logger?.LogDebug("Captured screen region {Width}x{Height} for source '{Source}'. Total captures: {Count}", width, height, source, _captureCount);

        return ScreenCaptureResult.Succeeded(bytes, width, height, source, isSyntheticFallback);
    }
}
