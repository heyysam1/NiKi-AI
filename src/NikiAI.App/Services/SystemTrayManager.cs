using System.IO;
using Microsoft.Extensions.Logging;
using NikiAI.App.Interop;

namespace NikiAI.App.Services;

/// <summary>
/// Manages the Windows system tray notification area icon using the standard Windows Desktop NotifyIcon.
/// Preserves the official Niki AI logo asset with high-quality scaling and handles click notifications.
/// </summary>
public class SystemTrayManager : IDisposable
{
    private readonly ILogger<SystemTrayManager>? _logger;
    private System.Windows.Forms.NotifyIcon? _notifyIcon;
    private System.Drawing.Icon? _icon;
    private IntPtr _hIcon = IntPtr.Zero;
    private bool _isDisposed;

    public event Action? TrayLeftClicked;
    public event Action? TrayRightClicked;

    public bool IsCreated => _notifyIcon?.Visible == true;

    public SystemTrayManager(ILogger<SystemTrayManager>? logger = null)
    {
        _logger = logger;
    }

    public void Initialize(IntPtr windowHandle, string logoPath)
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(SystemTrayManager));
        }

        if (!File.Exists(logoPath))
        {
            _logger?.LogError("Official logo file not found for tray icon at: {Path}", logoPath);
            return;
        }

        try
        {
            // High-quality bicubic downscaling of the official logo asset for crisp tray rendering
            using var original = new System.Drawing.Bitmap(logoPath);
            using var scaled = new System.Drawing.Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = System.Drawing.Graphics.FromImage(scaled))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                g.DrawImage(original, 0, 0, 32, 32);
            }

            _hIcon = scaled.GetHicon();
            _icon = System.Drawing.Icon.FromHandle(_hIcon);

            _notifyIcon = new System.Windows.Forms.NotifyIcon
            {
                Icon = _icon,
                Text = "Niki AI — Desktop Companion",
                Visible = true
            };

            _notifyIcon.MouseUp += (sender, args) =>
            {
                if (args.Button == System.Windows.Forms.MouseButtons.Left)
                {
                    _logger?.LogDebug("System tray left-click received.");
                    TrayLeftClicked?.Invoke();
                }
                else if (args.Button == System.Windows.Forms.MouseButtons.Right)
                {
                    _logger?.LogDebug("System tray right-click received.");
                    TrayRightClicked?.Invoke();
                }
            };

            _logger?.LogInformation("System tray icon successfully created with official brand logo.");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to initialize system tray icon.");
        }
    }

    public void ShowBalloonTip(string title, string text, int timeoutMs = 5000, System.Windows.Forms.ToolTipIcon icon = System.Windows.Forms.ToolTipIcon.Info)
    {
        if (_notifyIcon != null && _notifyIcon.Visible)
        {
            try
            {
                _notifyIcon.ShowBalloonTip(timeoutMs, title, text, icon);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to show balloon tip on system tray icon.");
            }
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
            _logger?.LogDebug("System tray icon removed.");
        }

        if (_icon != null)
        {
            _icon.Dispose();
            _icon = null;
        }

        if (_hIcon != IntPtr.Zero)
        {
            NativeMethods.DestroyIcon(_hIcon);
            _hIcon = IntPtr.Zero;
        }
    }
}
