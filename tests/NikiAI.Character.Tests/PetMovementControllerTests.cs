using System.Drawing;
using NikiAI.Character;
using NikiAI.Core.Character;
using Point = System.Drawing.Point;

namespace NikiAI.Character.Tests;

public class PetMovementControllerTests
{
    private class SimpleMockSurfaceManager : IPetSurfaceManager
    {
        public PetSurface PrimarySurface = new(
            "primary_surface",
            0,
            new Rectangle(0, 800, 1920, 200),
            PetSurfaceType.DesktopWorkArea,
            true,
            true
        );

        public PetSurface TargetSurface { get; set; }

        public SimpleMockSurfaceManager()
        {
            TargetSurface = PrimarySurface;
        }

        public IReadOnlyList<PetSurface> GetAvailableSurfaces() => new[] { PrimarySurface, TargetSurface };
        public PetSurface GetPrimaryWorkAreaSurface() => PrimarySurface;
        public PetSurface? GetSurfaceForWindow(nint hWnd) => TargetSurface.WindowHandle == hWnd ? TargetSurface : null;
        public PetSurface SelectTargetSurface(DesktopPetFollowMode mode, PetSurface? currentSurface = null) => TargetSurface;

        public Point ClampToSafeWorkArea(Point targetPoint, Size characterSize)
        {
            var x = Math.Clamp(targetPoint.X, 0, 1920 - characterSize.Width);
            var y = Math.Clamp(targetPoint.Y, 0, 1000 - characterSize.Height);
            return new Point(x, y);
        }

        public bool IsSurfaceValid(PetSurface surface) => surface.IsUsable;
    }

    [Fact]
    public void Controller_InitializesAtPrimarySurfaceCenter()
    {
        var surfaceManager = new SimpleMockSurfaceManager();
        var stateMachine = new CharacterStateMachine();
        var controller = new PetMovementController(surfaceManager, stateMachine, new Size(150, 100));

        Assert.Equal(DesktopPetFollowMode.StayOnSurface, controller.FollowMode);
        Assert.False(controller.IsMoving);
        Assert.Equal(surfaceManager.PrimarySurface.SurfaceId, controller.CurrentSurface.SurfaceId);
        Assert.Equal((1920 / 2) - 75, controller.CurrentPosition.X);
        Assert.Equal(800, controller.CurrentPosition.Y);
    }

    [Fact]
    public void Controller_SetPosition_DirectlyUpdatesAndClampsPosition()
    {
        var surfaceManager = new SimpleMockSurfaceManager();
        var stateMachine = new CharacterStateMachine();
        var controller = new PetMovementController(surfaceManager, stateMachine, new Size(150, 100));

        Point? reportedPoint = null;
        controller.PositionChanged += (s, p) => reportedPoint = p;

        controller.SetPosition(new Point(300, 400));

        Assert.Equal(300, controller.CurrentPosition.X);
        Assert.Equal(400, controller.CurrentPosition.Y);
        Assert.Equal(new Point(300, 400), reportedPoint);
        Assert.False(controller.IsMoving);
    }

    [Fact]
    public void Controller_Update_ComputesMovementIntentAndAnimatesTowardTarget()
    {
        var surfaceManager = new SimpleMockSurfaceManager();
        var stateMachine = new CharacterStateMachine();
        var controller = new PetMovementController(surfaceManager, stateMachine, new Size(150, 100));

        // Move target to far right
        surfaceManager.TargetSurface = new PetSurface(
            "target_win",
            0,
            new Rectangle(1400, 500, 400, 60),
            PetSurfaceType.ApplicationWindowTopEdge,
            true,
            true
        );

        MovementIntent? intent = null;
        controller.MovementIntentChanged += (s, i) => intent = i;

        // Tick movement
        controller.Update(TimeSpan.FromMilliseconds(100));

        Assert.True(controller.IsMoving);
        Assert.NotNull(intent);
        Assert.False(intent.FacingLeft); // moving right
        Assert.True(intent.DesiredState is CharacterState.Walk or CharacterState.Run);
        Assert.Equal(intent.DesiredState, stateMachine.CurrentState);
    }

    [Fact]
    public void Controller_ResetToSafeSurface_RestoresPrimaryDesktopPosition()
    {
        var surfaceManager = new SimpleMockSurfaceManager();
        var stateMachine = new CharacterStateMachine();
        var controller = new PetMovementController(surfaceManager, stateMachine, new Size(150, 100));

        controller.SetPosition(new Point(100, 100));
        controller.ResetToSafeSurface();

        Assert.Equal(surfaceManager.PrimarySurface.SurfaceId, controller.CurrentSurface.SurfaceId);
        Assert.Equal(800, controller.CurrentPosition.Y);
    }

    [Fact]
    public void Controller_SetPosition_FollowedByUpdate_PreservesUserPositionWhenOnSameSurface()
    {
        var surfaceManager = new SimpleMockSurfaceManager();
        var stateMachine = new CharacterStateMachine();
        var controller = new PetMovementController(surfaceManager, stateMachine, new Size(150, 100));

        controller.SetPosition(new Point(300, 400));
        Assert.Equal(300, controller.CurrentPosition.X);
        Assert.Equal(400, controller.CurrentPosition.Y);

        // Update tick should NOT overwrite user's chosen position or snap back to center
        controller.Update(TimeSpan.FromMilliseconds(50));

        Assert.False(controller.IsMoving);
        Assert.Equal(300, controller.CurrentPosition.X);
        Assert.Equal(400, controller.CurrentPosition.Y);
    }

    [Fact]
    public void Controller_FollowActiveWindow_TransitionsToTargetWindowSurface()
    {
        var surfaceManager = new SimpleMockSurfaceManager();
        var stateMachine = new CharacterStateMachine();
        var controller = new PetMovementController(surfaceManager, stateMachine, new Size(150, 100))
        {
            FollowMode = DesktopPetFollowMode.FollowActiveWindow
        };

        controller.SetPosition(new Point(200, 800));

        // When a new active window surface appears
        surfaceManager.TargetSurface = new PetSurface(
            "active_win_surface",
            0,
            new Rectangle(600, 200, 800, 50),
            PetSurfaceType.ApplicationWindowTopEdge,
            true,
            true
        );

        controller.Update(TimeSpan.FromMilliseconds(50));

        Assert.True(controller.IsMoving);
        Assert.Equal("active_win_surface", controller.CurrentSurface.SurfaceId);
    }
}
