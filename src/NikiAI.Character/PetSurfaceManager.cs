using System.Drawing;
using NikiAI.Core.Character;
using Point = System.Drawing.Point;

namespace NikiAI.Character;

/// <summary>
/// Manages spatial surfaces across monitors, taskbars, and top-level application windows.
/// Implements safe fallback, multi-monitor clamping, and surface invalidation recovery.
/// </summary>
public class PetSurfaceManager : IPetSurfaceManager
{
    private readonly IWindowObserver _windowObserver;

    public PetSurfaceManager(IWindowObserver windowObserver)
    {
        _windowObserver = windowObserver ?? throw new ArgumentNullException(nameof(windowObserver));
    }

    public PetSurface GetPrimaryWorkAreaSurface()
    {
        var workAreas = _windowObserver.GetMonitorWorkAreas();
        var primaryArea = workAreas.Count > 0 ? workAreas[0] : new Rectangle(0, 0, 1920, 1040);

        // Safe resting surface along the bottom of the primary work area
        var surfaceBounds = new Rectangle(
            primaryArea.Left,
            primaryArea.Bottom - 120,
            primaryArea.Width,
            120
        );

        return new PetSurface(
            SurfaceId: "primary_desktop_workarea",
            MonitorIndex: 0,
            Bounds: surfaceBounds,
            SurfaceType: PetSurfaceType.DesktopWorkArea,
            IsUsable: true,
            CanWalkOn: true
        );
    }

    public IReadOnlyList<PetSurface> GetAvailableSurfaces()
    {
        var surfaces = new List<PetSurface>
        {
            GetPrimaryWorkAreaSurface()
        };

        var workAreas = _windowObserver.GetMonitorWorkAreas();
        for (int i = 0; i < workAreas.Count; i++)
        {
            var area = workAreas[i];
            // Taskbar edge surface
            surfaces.Add(new PetSurface(
                SurfaceId: $"taskbar_monitor_{i}",
                MonitorIndex: i,
                Bounds: new Rectangle(area.Left, area.Bottom - 40, area.Width, 40),
                SurfaceType: PetSurfaceType.TaskbarArea,
                IsUsable: true,
                CanWalkOn: true
            ));
        }

        var windows = _windowObserver.GetTopLevelWindows();
        foreach (var win in windows)
        {
            if (win.IsMinimized || win.Bounds.Width < 200 || win.Bounds.Height < 150)
            {
                continue;
            }

            // Top edge of the window for sitting/standing
            var topEdgeBounds = new Rectangle(
                win.Bounds.Left,
                Math.Max(0, win.Bounds.Top - 60),
                win.Bounds.Width,
                60
            );

            surfaces.Add(new PetSurface(
                SurfaceId: $"win_top_{win.Handle:X}",
                MonitorIndex: 0,
                Bounds: topEdgeBounds,
                SurfaceType: PetSurfaceType.ApplicationWindowTopEdge,
                IsUsable: true,
                CanWalkOn: true,
                WindowHandle: win.Handle,
                ProcessName: win.ProcessName
            ));
        }

        return surfaces;
    }

    public PetSurface? GetSurfaceForWindow(nint hWnd)
    {
        if (hWnd == nint.Zero) return null;

        var windows = _windowObserver.GetTopLevelWindows();
        var win = windows.FirstOrDefault(w => w.Handle == hWnd);
        if (win == null || win.IsMinimized) return null;

        return new PetSurface(
            SurfaceId: $"win_top_{win.Handle:X}",
            MonitorIndex: 0,
            Bounds: new Rectangle(win.Bounds.Left, Math.Max(0, win.Bounds.Top - 60), win.Bounds.Width, 60),
            SurfaceType: PetSurfaceType.ApplicationWindowTopEdge,
            IsUsable: true,
            CanWalkOn: true,
            WindowHandle: win.Handle,
            ProcessName: win.ProcessName
        );
    }

    public PetSurface SelectTargetSurface(DesktopPetFollowMode mode, PetSurface? currentSurface = null)
    {
        switch (mode)
        {
            case DesktopPetFollowMode.FollowActiveWindow:
            {
                var activeWin = _windowObserver.GetActiveWindow();
                if (activeWin != null &&
                    !activeWin.IsMinimized &&
                    activeWin.Bounds.Width >= 200 &&
                    activeWin.Bounds.Height >= 150 &&
                    activeWin.ProcessName != "explorer")
                {
                    var surface = GetSurfaceForWindow(activeWin.Handle);
                    if (surface != null && surface.IsUsable)
                    {
                        return surface;
                    }
                }

                // If active window is invalid or minimized, safely recover to primary desktop work area
                return GetPrimaryWorkAreaSurface();
            }

            case DesktopPetFollowMode.StayOnSurface:
            {
                if (currentSurface != null && IsSurfaceValid(currentSurface))
                {
                    return currentSurface;
                }
                return GetPrimaryWorkAreaSurface();
            }

            case DesktopPetFollowMode.RoamDesktop:
            {
                var available = GetAvailableSurfaces();
                return available.Count > 0 ? available[0] : GetPrimaryWorkAreaSurface();
            }

            case DesktopPetFollowMode.Sleep:
            default:
                return GetPrimaryWorkAreaSurface();
        }
    }

    public Point ClampToSafeWorkArea(Point targetPoint, Size characterSize)
    {
        var workAreas = _windowObserver.GetMonitorWorkAreas();
        if (workAreas.Count == 0)
        {
            return targetPoint;
        }

        // Find work area containing or closest to targetPoint
        var bestArea = workAreas[0];
        var shortestDistance = double.MaxValue;

        foreach (var area in workAreas)
        {
            if (area.Contains(targetPoint))
            {
                bestArea = area;
                break;
            }

            var centerX = area.Left + area.Width / 2;
            var centerY = area.Top + area.Height / 2;
            var dist = Math.Pow(centerX - targetPoint.X, 2) + Math.Pow(centerY - targetPoint.Y, 2);
            if (dist < shortestDistance)
            {
                shortestDistance = dist;
                bestArea = area;
            }
        }

        var clampedX = Math.Clamp(targetPoint.X, bestArea.Left, Math.Max(bestArea.Left, bestArea.Right - characterSize.Width));
        var clampedY = Math.Clamp(targetPoint.Y, bestArea.Top, Math.Max(bestArea.Top, bestArea.Bottom - characterSize.Height));

        return new Point(clampedX, clampedY);
    }

    public bool IsSurfaceValid(PetSurface surface)
    {
        if (surface == null || !surface.IsUsable) return false;

        if (surface.SurfaceType is PetSurfaceType.DesktopWorkArea or PetSurfaceType.TaskbarArea)
        {
            return true;
        }

        if (surface.WindowHandle != nint.Zero)
        {
            var windows = _windowObserver.GetTopLevelWindows();
            var exists = windows.Any(w => w.Handle == surface.WindowHandle && !w.IsMinimized);
            return exists;
        }

        return false;
    }
}
