using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NikiAI.App.Interop;

/// <summary>
/// Win32 Native P/Invoke methods and structures for global hotkeys and system tray notification icons.
/// </summary>
public static class NativeMethods
{
    public const int WM_HOTKEY = 0x0312;
    public const int WM_USER = 0x0400;
    public const int WM_TRAYICON = WM_USER + 101;

    public const int WM_LBUTTONUP = 0x0202;
    public const int WM_RBUTTONUP = 0x0205;

    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;

    public const uint NIM_ADD = 0x00000000;
    public const uint NIM_MODIFY = 0x00000001;
    public const uint NIM_DELETE = 0x00000002;

    public const uint NIF_MESSAGE = 0x00000001;
    public const uint NIF_ICON = 0x00000002;
    public const uint NIF_TIP = 0x00000004;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public uint uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public int uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ICONINFO
    {
        public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr CreateIconIndirect(ref ICONINFO iconInfo);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern IntPtr CreateBitmap(int nWidth, int nHeight, uint nPlanes, uint nBitCount, byte[]? lpBits);

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr CreateIconFromResourceEx(
        byte[] pbIconBits,
        uint cbIconBits,
        bool fIcon,
        uint dwVersion,
        int cxDesired,
        int cyDesired,
        uint uFlags);

    /// <summary>
    /// Creates a sharp Win32 HICON from the official Niki AI logo PNG file
    /// with high-quality scaling preserving the approved logo.
    /// Uses user32 CreateIconFromResourceEx which natively supports PNG assets.
    /// </summary>
    public static IntPtr CreateSharpTrayHIcon(string pngFilePath, int targetSize = 32)
    {
        if (!File.Exists(pngFilePath))
        {
            throw new FileNotFoundException($"Official logo file not found at: {pngFilePath}");
        }

        // If target size is requested, we can use WPF to resize cleanly to target size (e.g. 32x32)
        // and encode to PNG memory stream, then CreateIconFromResourceEx turns it directly into an HICON.
        var uri = new Uri(Path.GetFullPath(pngFilePath), UriKind.Absolute);
        var bitmapImage = new BitmapImage();
        bitmapImage.BeginInit();
        bitmapImage.UriSource = uri;
        bitmapImage.DecodePixelWidth = targetSize;
        bitmapImage.DecodePixelHeight = targetSize;
        bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
        bitmapImage.EndInit();
        bitmapImage.Freeze();

        var pngEncoder = new PngBitmapEncoder();
        pngEncoder.Frames.Add(BitmapFrame.Create(bitmapImage));

        using var ms = new MemoryStream();
        pngEncoder.Save(ms);
        byte[] pngBytes = ms.ToArray();

        // 0x00030000 is the version for Windows 3.0+ (standard for CreateIconFromResourceEx)
        var hIcon = CreateIconFromResourceEx(pngBytes, (uint)pngBytes.Length, true, 0x00030000, targetSize, targetSize, 0);

        if (hIcon == IntPtr.Zero)
        {
            // Fallback: try raw file bytes directly
            byte[] rawBytes = File.ReadAllBytes(pngFilePath);
            hIcon = CreateIconFromResourceEx(rawBytes, (uint)rawBytes.Length, true, 0x00030000, targetSize, targetSize, 0);
        }

        return hIcon;
    }
}
