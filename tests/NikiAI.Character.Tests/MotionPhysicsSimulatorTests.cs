using NikiAI.Character;
using NikiAI.Core.Character;
using Xunit;

namespace NikiAI.Character.Tests;

public class MotionPhysicsSimulatorTests
{
    [Fact]
    public void CalculateOffset_Bounce_AppliesSquashAndStretchPhases()
    {
        var simulator = new MotionPhysicsSimulator();
        var duration = TimeSpan.FromMilliseconds(500);

        // Phase 1 (Anticipation / squash down): 5% progress
        var p1 = simulator.CalculateOffset(MotionPrimitive.Bounce, TimeSpan.FromMilliseconds(25), duration, false);
        Assert.True(p1.OffsetY >= 0.0, "Anticipation should shift down or stay zero");
        Assert.True(p1.ScaleY <= 1.0, "Anticipation should squash vertical scale");
        Assert.True(p1.ScaleX >= 1.0, "Anticipation should stretch horizontal scale");

        // Phase 2 (Flight / peak stretch): 50% progress
        var p2 = simulator.CalculateOffset(MotionPrimitive.Bounce, TimeSpan.FromMilliseconds(250), duration, false);
        Assert.True(p2.OffsetY < 0.0, "Flight should shift up (negative Y offset)");
        Assert.True(p2.ScaleY >= 1.0, "Flight should stretch vertical scale");

        // End of step: returns identity
        var pEnd = simulator.CalculateOffset(MotionPrimitive.Bounce, duration, duration, false);
        Assert.True(pEnd.IsIdentity);
    }

    [Fact]
    public void CalculateOffset_ReducedMotion_ReturnsStrictIdentity()
    {
        var simulator = new MotionPhysicsSimulator { ReducedMotion = true };
        var duration = TimeSpan.FromMilliseconds(500);

        var offset = simulator.CalculateOffset(MotionPrimitive.Bounce, TimeSpan.FromMilliseconds(250), duration, true);
        Assert.Equal(0.0, offset.OffsetX);
        Assert.Equal(0.0, offset.OffsetY);
        Assert.Equal(1.0, offset.ScaleX);
        Assert.Equal(1.0, offset.ScaleY);
        Assert.True(offset.IsIdentity);

        var simStep = simulator.SimulateStep(MotionPrimitive.Bounce, 0.5, 1.0);
        Assert.True(simStep.IsIdentity);
    }

    [Fact]
    public void Reset_ReturnsExactIdentityTransform()
    {
        var simulator = new MotionPhysicsSimulator();
        var reset = simulator.Reset();

        Assert.Equal(0.0, reset.OffsetX);
        Assert.Equal(0.0, reset.OffsetY);
        Assert.Equal(1.0, reset.ScaleX);
        Assert.Equal(1.0, reset.ScaleY);
        Assert.True(reset.IsIdentity);
    }

    [Theory]
    [InlineData(MotionPrimitive.ShiftWeight)]
    [InlineData(MotionPrimitive.Stretch)]
    [InlineData(MotionPrimitive.HeadTiltLeft)]
    [InlineData(MotionPrimitive.HeadTiltRight)]
    [InlineData(MotionPrimitive.GuardStance)]
    public void CalculateOffset_Primitives_ProduceSmoothOffsets(MotionPrimitive primitive)
    {
        var simulator = new MotionPhysicsSimulator();
        var duration = TimeSpan.FromMilliseconds(400);

        var offset = simulator.CalculateOffset(primitive, TimeSpan.FromMilliseconds(200), duration, false);
        Assert.NotNull(offset);
        Assert.False(double.IsNaN(offset.OffsetX));
        Assert.False(double.IsNaN(offset.OffsetY));
        Assert.False(double.IsNaN(offset.ScaleX));
        Assert.False(double.IsNaN(offset.ScaleY));
    }
}
