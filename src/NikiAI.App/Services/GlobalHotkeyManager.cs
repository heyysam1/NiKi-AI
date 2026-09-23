using System.Runtime.InteropServices;
using System.Windows.Interop;
using Microsoft.Extensions.Logging;
using NikiAI.App.Interop;

namespace NikiAI.App.Services;

/// <summary>
/// Manages global Windows hotkey registration and invocation.
/// Supports preferred hotkey (Win + Alt + N) and fallback (Ctrl + Shift + N, Alt + Shift + N).
/// </summary>
public class GlobalHotkeyManager : IDisposable
{
    private const int HotkeyId = 9001;
    private const uint VK_N = 0x4E;

    private readonly ILogger<GlobalHotkeyManager>? _logger;
    private IntPtr _windowHandle;
    private HwndSource? _hwndSource;
    private bool _isRegistered;

    public string ActiveHotkeyDescription { get; private set; } = "None";
    public bool IsRegistered => _isRegistered;

    public event Action? HotkeyPressed;
    public event Action<string>? WorkflowHotkeyPressed;
    private readonly Dictionary<int, string> _workflowHotkeys = new();

    public GlobalHotkeyManager(ILogger<GlobalHotkeyManager>? logger = null)
    {
        _logger = logger;
    }

    public void Initialize(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            throw new ArgumentException("Window handle cannot be zero.", nameof(windowHandle));
        }

        _windowHandle = windowHandle;
        _hwndSource = HwndSource.FromHwnd(_windowHandle);
        _hwndSource?.AddHook(WndProc);

        RegisterWithFallback();
    }

    private void RegisterWithFallback()
    {
        // 1. Preferred Hotkey: Win + Alt + N
        if (TryRegister(NativeMethods.MOD_WIN | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT, VK_N, "Win + Alt + N"))
        {
            return;
        }

        // 2. Primary Fallback: Ctrl + Shift + N
        var lastErr = Marshal.GetLastWin32Error();
        _logger?.LogWarning("Preferred global hotkey 'Win + Alt + N' could not be registered (Win32 Error: {Err}). Attempting fallback 'Ctrl + Shift + N'...", lastErr);

        if (TryRegister(NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT | NativeMethods.MOD_NOREPEAT, VK_N, "Ctrl + Shift + N"))
        {
            return;
        }

        // 3. Secondary Fallback: Alt + Shift + N
        lastErr = Marshal.GetLastWin32Error();
        _logger?.LogWarning("Fallback 'Ctrl + Shift + N' could not be registered (Win32 Error: {Err}). Attempting secondary fallback 'Alt + Shift + N'...", lastErr);

        if (TryRegister(NativeMethods.MOD_ALT | NativeMethods.MOD_SHIFT | NativeMethods.MOD_NOREPEAT, VK_N, "Alt + Shift + N"))
        {
            return;
        }

        lastErr = Marshal.GetLastWin32Error();
        _logger?.LogError("Failed to register any global hotkey. All hotkey attempts failed with conflicts (Win32 Error: {Err}).", lastErr);
        ActiveHotkeyDescription = "Unavailable (Conflict)";
        _isRegistered = false;
    }

    private bool TryRegister(uint modifiers, uint vk, string description)
    {
        var success = NativeMethods.RegisterHotKey(_windowHandle, HotkeyId, modifiers, vk);
        if (success)
        {
            _isRegistered = true;
            ActiveHotkeyDescription = description;
            _logger?.LogInformation("Successfully registered global hotkey: {Hotkey}", description);
            return true;
        }
        return false;
    }

    public bool RegisterWorkflowHotkey(int hotkeyId, uint modifiers, uint vk, string workflowId)
    {
        if (_windowHandle == IntPtr.Zero || string.IsNullOrWhiteSpace(workflowId))
        {
            return false;
        }

        var success = NativeMethods.RegisterHotKey(_windowHandle, hotkeyId, modifiers, vk);
        if (success)
        {
            _workflowHotkeys[hotkeyId] = workflowId;
            _logger?.LogInformation("Successfully registered workflow hotkey ID {Id} for workflow {WorkflowId}", hotkeyId, workflowId);
            return true;
        }

        var lastErr = Marshal.GetLastWin32Error();
        _logger?.LogWarning("Failed to register workflow hotkey ID {Id} for workflow {WorkflowId} (Win32 Error: {Err})", hotkeyId, workflowId, lastErr);
        return false;
    }

    /// <summary>
    /// Programmatically dispatches a workflow hotkey invocation without requiring Win32 hardware message simulation.
    /// Used for deterministic testing and runtime verification.
    /// </summary>
    public void TriggerWorkflowHotkey(string workflowId)
    {
        if (!string.IsNullOrWhiteSpace(workflowId))
        {
            _logger?.LogDebug("Workflow hotkey triggered for workflow {WorkflowId}.", workflowId);
            WorkflowHotkeyPressed?.Invoke(workflowId);
        }
    }

    /// <summary>
    /// Simulates hotkey invocation by hotkey ID.
    /// </summary>
    public void SimulateHotkey(int hotkeyId)
    {
        if (_workflowHotkeys.TryGetValue(hotkeyId, out var workflowId))
        {
            TriggerWorkflowHotkey(workflowId);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY)
        {
            var id = wParam.ToInt32();
            if (id == HotkeyId)
            {
                _logger?.LogDebug("Global hotkey '{Hotkey}' triggered.", ActiveHotkeyDescription);
                HotkeyPressed?.Invoke();
                handled = true;
            }
            else if (_workflowHotkeys.TryGetValue(id, out var workflowId))
            {
                _logger?.LogDebug("Workflow hotkey {Id} triggered for workflow {WorkflowId}.", id, workflowId);
                WorkflowHotkeyPressed?.Invoke(workflowId);
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_isRegistered && _windowHandle != IntPtr.Zero)
        {
            NativeMethods.UnregisterHotKey(_windowHandle, HotkeyId);
            _isRegistered = false;
            _logger?.LogDebug("Unregistered global hotkey.");
        }

        foreach (var id in _workflowHotkeys.Keys)
        {
            if (_windowHandle != IntPtr.Zero)
            {
                NativeMethods.UnregisterHotKey(_windowHandle, id);
            }
        }
        _workflowHotkeys.Clear();

        if (_hwndSource != null)
        {
            _hwndSource.RemoveHook(WndProc);
            _hwndSource = null;
        }
    }
}
