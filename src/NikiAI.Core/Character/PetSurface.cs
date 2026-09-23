using System.Drawing;

namespace NikiAI.Core.Character;

/// <summary>
/// Types of surfaces available in the desktop environment for pet movement and idling.
/// </summary>
public enum PetSurfaceType
{
    DesktopWorkArea,
    TaskbarArea,
    ApplicationWindowTopEdge,
    ApplicationWindowBottomEdge,
    ApplicationWindowLeftEdge,
    ApplicationWindowRightEdge,
    Fallback
}

/// <summary>
/// Autonomous movement and target following modes for Desktop Pet Stage B.
/// </summary>
public enum DesktopPetFollowMode
{
    StayOnSurface,
    FollowActiveWindow,
    RoamDesktop,
    Sleep
}

/// <summary>
/// Represents a discrete physical surface in the desktop workspace.
/// </summary>
public record PetSurface(
    string SurfaceId,
    int MonitorIndex,
    Rectangle Bounds,
    PetSurfaceType SurfaceType,
    bool IsUsable = true,
    bool CanWalkOn = true,
    nint WindowHandle = 0,
    string? ProcessName = null
);

/// <summary>
/// Minimal geometry and metadata observed from top-level application windows.
/// Strictly limited to layout geometry without reading window contents.
/// </summary>
public record WindowGeometry(
    nint Handle,
    string Title,
    string ProcessName,
    Rectangle Bounds,
    bool IsActive,
    bool IsMinimized,
    int ZOrder = 0
);

/// <summary>
/// Intent and directional parameters for pet movement across desktop surfaces.
/// </summary>
public record MovementIntent(
    double TargetX,
    double TargetY,
    bool FacingLeft,
    CharacterState DesiredState,
    double Velocity = 1.0
);

/// <summary>
/// Observes native desktop windows and monitor geometry without intrusive hooks.
/// </summary>
public interface IWindowObserver
{
    IReadOnlyList<WindowGeometry> GetTopLevelWindows();

    WindowGeometry? GetActiveWindow();

    IReadOnlyList<Rectangle> GetMonitorWorkAreas();

    Rectangle GetVirtualScreenBounds();

    event EventHandler? ActiveWindowChanged;
    event EventHandler? DisplayTopologyChanged;
}

/// <summary>
/// Computes and manages safe surfaces across displays, active windows, and taskbar regions.
/// </summary>
public interface IPetSurfaceManager
{
    IReadOnlyList<PetSurface> GetAvailableSurfaces();

    PetSurface GetPrimaryWorkAreaSurface();

    PetSurface? GetSurfaceForWindow(nint hWnd);

    PetSurface SelectTargetSurface(DesktopPetFollowMode mode, PetSurface? currentSurface = null);

    Point ClampToSafeWorkArea(Point targetPoint, Size characterSize);

    bool IsSurfaceValid(PetSurface surface);
}

/// <summary>
/// High-level movement controller integrating Character Runtime with Desktop Pet Stage B.
/// </summary>
public interface IPetMovementController
{
    DesktopPetFollowMode FollowMode { get; set; }

    bool IsMoving { get; }

    Point CurrentPosition { get; }

    PetSurface CurrentSurface { get; }

    MovementIntent? CurrentIntent { get; }

    void SetPosition(Point newPosition);

    void Update(TimeSpan elapsed);

    void ResetToSafeSurface();

    event EventHandler<Point>? PositionChanged;
    event EventHandler<MovementIntent>? MovementIntentChanged;
}
