using System.Drawing;

namespace NikiAI.Core.Vision;

/// <summary>
/// Service abstraction for on-demand screen and window capture.
/// Strictly enforces privacy controls, executes on-demand only (no continuous capture),
/// and reuses Phase 9 geometry abstractions.
/// </summary>
public interface IScreenCaptureService
{
    /// <summary>
    /// Indicates whether screen capture capabilities are available on the current OS/environment.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// User privacy gate controlling whether screen capture operations are permitted.
    /// When false, all capture requests fail closed immediately.
    /// </summary>
    bool ScreenAwarenessEnabled { get; set; }

    /// <summary>
    /// Monotonically increasing count of completed screen capture operations.
    /// Used for behavioral verification (asserting 0 captures during startup or unapproved flows).
    /// </summary>
    int CaptureCount { get; }

    /// <summary>
    /// Captures the primary display monitor on demand.
    /// </summary>
    Task<ScreenCaptureResult> CapturePrimaryScreenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Captures the currently active foreground window using Phase 9 geometry.
    /// </summary>
    Task<ScreenCaptureResult> CaptureActiveWindowAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Captures the specific window identified by its native handle.
    /// </summary>
    Task<ScreenCaptureResult> CaptureWindowAsync(nint hWnd, CancellationToken cancellationToken = default);

    /// <summary>
    /// Captures an arbitrary screen region defined by Phase 9 Rectangle coordinates.
    /// </summary>
    Task<ScreenCaptureResult> CaptureRegionAsync(Rectangle bounds, CancellationToken cancellationToken = default);
}
