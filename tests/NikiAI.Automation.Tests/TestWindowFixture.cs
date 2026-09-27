using System.Runtime.InteropServices;

namespace NikiAI.Automation.Tests;

public sealed class TestWindowFixture : IDisposable
{
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowExW(
        int dwExStyle, string lpClassName, string lpWindowName, int dwStyle,
        int x, int y, int nWidth, int nHeight,
        nint hWndParent, nint hMenu, nint hInstance, nint lpParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    public nint Hwnd { get; }
    public string Title { get; } = "Niki Automation Test Window";

    public TestWindowFixture()
    {
        // WS_OVERLAPPEDWINDOW | WS_VISIBLE = 0x00CF0000 | 0x10000000
        Hwnd = CreateWindowExW(0, "STATIC", Title, 0x10CF0000, 100, 100, 500, 400, nint.Zero, nint.Zero, nint.Zero, nint.Zero);
        if (Hwnd != nint.Zero)
        {
            ShowWindow(Hwnd, 5); // SW_SHOW
        }
    }

    public void Dispose()
    {
        if (Hwnd != nint.Zero)
        {
            DestroyWindow(Hwnd);
        }
    }
}
