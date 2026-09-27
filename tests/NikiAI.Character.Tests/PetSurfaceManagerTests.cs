using System.Drawing;
using NikiAI.Character;
using NikiAI.Core.Character;
using Point = System.Drawing.Point;

namespace NikiAI.Character.Tests;

public class PetSurfaceManagerTests
{
    private class MockWindowObserver : IWindowObserver
    {
#pragma warning disable CS0067
        public event EventHandler? ActiveWindowChanged;
        public event EventHandler? DisplayTopologyChanged;
#pragma warning restore CS0067

        public List<WindowGeometry> TopWindows { get; set; } = new();
        public WindowGeometry? ActiveWindow { get; set; }
        public List<Rectangle> WorkAreas { get; set; } = new()
        {
            new Rectangle(0, 0, 1920, 1040),
            new Rectangle(1920, 0, 1920, 1040)
        };

        public IReadOnlyList<WindowGeometry> GetTopLevelWindows() => TopWindows;
        public WindowGeometry? GetActiveWindow() => ActiveWindow;
        public IReadOnlyList<Rectangle> GetMonitorWorkAreas() => WorkAreas;
        public Rectangle GetVirtualScreenBounds() => new Rectangle(0, 0, 3840, 1080);
    }

    [Fact]
    public void GetPrimaryWorkAreaSurface_ReturnsValidDesktopSurface()
    {
        var observer = new MockWindowObserver();
        var manager = new PetSurfaceManager(observer);

        var surface = manager.GetPrimaryWorkAreaSurface();
        Assert.NotNull(surface);
        Assert.Equal(PetSurfaceType.DesktopWorkArea, surface.SurfaceType);
        Assert.True(surface.IsUsable);
        Assert.True(surface.CanWalkOn);
        Assert.Equal(0, surface.MonitorIndex);
        Assert.Equal(1920, surface.Bounds.Width);
    }

    [Fact]
    public void GetAvailableSurfaces_IncludesTaskbarsAndApplicationWindows()
    {
        var observer = new MockWindowObserver
        {
            TopWindows = new List<WindowGeometry>
            {
                new WindowGeometry((nint)0x1001, "Notepad", "notepad", new Rectangle(100, 100, 800, 600), false, false)
            }
        };
        var manager = new PetSurfaceManager(observer);

        var surfaces = manager.GetAvailableSurfaces();
        Assert.NotNull(surfaces);
        Assert.Contains(surfaces, s => s.SurfaceType == PetSurfaceType.DesktopWorkArea);
        Assert.Contains(surfaces, s => s.SurfaceType == PetSurfaceType.TaskbarArea);
        Assert.Contains(surfaces, s => s.SurfaceType == PetSurfaceType.ApplicationWindowTopEdge && s.ProcessName == "notepad");
    }

    [Fact]
    public void SelectTargetSurface_FollowActiveWindow_SelectsWindowWhenValid()
    {
        var winGeom = new WindowGeometry((nint)0x2002, "Visual Studio Code", "Code", new Rectangle(200, 150, 1000, 700), true, false);
        var observer = new MockWindowObserver
        {
            TopWindows = new List<WindowGeometry> { winGeom },
            ActiveWindow = winGeom
        };
        var manager = new PetSurfaceManager(observer);

        var targetSurface = manager.SelectTargetSurface(DesktopPetFollowMode.FollowActiveWindow);
        Assert.NotNull(targetSurface);
        Assert.Equal(PetSurfaceType.ApplicationWindowTopEdge, targetSurface.SurfaceType);
        Assert.Equal(winGeom.Handle, targetSurface.WindowHandle);
    }

    [Fact]
    public void SelectTargetSurface_FollowActiveWindow_FallsBackWhenWindowMinimized()
    {
        var winGeom = new WindowGeometry((nint)0x3003, "Minimized App", "app", new Rectangle(0, 0, 500, 400), true, true);
        var observer = new MockWindowObserver
        {
            TopWindows = new List<WindowGeometry> { winGeom },
            ActiveWindow = winGeom
        };
        var manager = new PetSurfaceManager(observer);

        var targetSurface = manager.SelectTargetSurface(DesktopPetFollowMode.FollowActiveWindow);
        Assert.NotNull(targetSurface);
        Assert.Equal(PetSurfaceType.DesktopWorkArea, targetSurface.SurfaceType);
    }

    [Fact]
    public void ClampToSafeWorkArea_ClampsOffscreenPoints()
    {
        var observer = new MockWindowObserver();
        var manager = new PetSurfaceManager(observer);

        var charSize = new Size(150, 100);
        var negativePoint = new Point(-500, -200);
        var clamped = manager.ClampToSafeWorkArea(negativePoint, charSize);

        Assert.Equal(0, clamped.X);
        Assert.Equal(0, clamped.Y);

        var beyondRightPoint = new Point(5000, 5000);
        var clampedBeyond = manager.ClampToSafeWorkArea(beyondRightPoint, charSize);

        // Closest is monitor 1 (1920 to 3840)
        Assert.Equal(3840 - charSize.Width, clampedBeyond.X);
        Assert.Equal(1040 - charSize.Height, clampedBeyond.Y);
    }

    [Fact]
    public void IsSurfaceValid_DetectsInvalidWindowSurface()
    {
        var observer = new MockWindowObserver
        {
            TopWindows = new List<WindowGeometry>()
        };
        var manager = new PetSurfaceManager(observer);

        var deadWindowSurface = new PetSurface(
            "win_dead",
            0,
            new Rectangle(100, 100, 300, 50),
            PetSurfaceType.ApplicationWindowTopEdge,
            true,
            true,
            (nint)0x9999
        );

        Assert.False(manager.IsSurfaceValid(deadWindowSurface));

        var desktopSurface = manager.GetPrimaryWorkAreaSurface();
        Assert.True(manager.IsSurfaceValid(desktopSurface));
    }
}
