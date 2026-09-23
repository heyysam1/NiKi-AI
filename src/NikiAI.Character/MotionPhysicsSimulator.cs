using NikiAI.Core.Character;

namespace NikiAI.Character;

/// <summary>
/// 2D visual transform offsets computed by the physics simulator.
/// Must be applied strictly to the visual rendering layer (RenderTransform).
/// Must NEVER modify CompanionWindow.Left/Top or PetSurface/navigation coordinates.
/// </summary>
public record PhysicsOffset(double OffsetX, double OffsetY, double ScaleX, double ScaleY)
{
    public static PhysicsOffset Identity { get; } = new(0.0, 0.0, 1.0, 1.0);

    public bool IsIdentity =>
        Math.Abs(OffsetX) < 0.001 &&
        Math.Abs(OffsetY) < 0.001 &&
        Math.Abs(ScaleX - 1.0) < 0.001 &&
        Math.Abs(ScaleY - 1.0) < 0.001;
}

/// <summary>
/// 2D animation physics simulator computing temporary visual character transform offsets
/// (inertia, weight shift, anticipation, follow-through, squash & stretch).
/// Evaluated strictly on-demand during active primitive playback; ceases immediately upon completion.
/// Has zero continuous background loops.
/// </summary>
public class MotionPhysicsSimulator
{
    public bool ReducedMotion { get; set; }

    /// <summary>
    /// Resets the physics simulation and returns the identity transform offset.
    /// </summary>
    public PhysicsOffset Reset() => PhysicsOffset.Identity;

    /// <summary>
    /// Simulates a step of motion physics for a given primitive at a specified progress (0.0 to 1.0).
    /// </summary>
    public PhysicsOffset SimulateStep(MotionPrimitive primitive, double progress, double intensity = 1.0)
    {
        var duration = TimeSpan.FromMilliseconds(500);
        var elapsed = TimeSpan.FromMilliseconds(500 * Math.Clamp(progress, 0.0, 1.0));
        var offset = CalculateOffset(primitive, elapsed, duration, ReducedMotion);
        if (Math.Abs(intensity - 1.0) > 0.01 && !offset.IsIdentity)
        {
            return new PhysicsOffset(offset.OffsetX * intensity, offset.OffsetY * intensity, 1.0 + (offset.ScaleX - 1.0) * intensity, 1.0 + (offset.ScaleY - 1.0) * intensity);
        }
        return offset;
    }

    /// <summary>
    /// Computes the visual transform offset for a given motion primitive at a point in time.
    /// Returns PhysicsOffset.Identity if Reduced Motion is enabled or the step has finished.
    /// </summary>
    public PhysicsOffset CalculateOffset(
        MotionPrimitive primitive,
        TimeSpan elapsedInStep,
        TimeSpan stepDuration,
        bool reducedMotion)
    {
        // Reduced motion guarantee: zero offsets, unit scale
        if (reducedMotion)
        {
            return PhysicsOffset.Identity;
        }

        // Idle or past duration: immediately cease physics and return identity
        if (primitive == MotionPrimitive.Idle || stepDuration <= TimeSpan.Zero || elapsedInStep >= stepDuration || elapsedInStep < TimeSpan.Zero)
        {
            return PhysicsOffset.Identity;
        }

        var progress = Math.Clamp(elapsedInStep.TotalSeconds / stepDuration.TotalSeconds, 0.0, 1.0);

        return primitive switch
        {
            MotionPrimitive.Bounce => CalculateBouncePhysics(progress),
            MotionPrimitive.ShiftWeight => CalculateWeightShiftPhysics(progress),
            MotionPrimitive.Stretch => CalculateStretchPhysics(progress),
            MotionPrimitive.HeadTiltLeft => CalculateHeadTiltPhysics(progress, -1.0),
            MotionPrimitive.HeadTiltRight => CalculateHeadTiltPhysics(progress, 1.0),
            MotionPrimitive.LeanForward => CalculateLeanForwardPhysics(progress),
            MotionPrimitive.StepBack => CalculateStepBackPhysics(progress),
            MotionPrimitive.PawStep => CalculatePawStepPhysics(progress),
            MotionPrimitive.TailWag => CalculateTailWagPhysics(progress),
            MotionPrimitive.FistPump => CalculateFistPumpPhysics(progress),
            MotionPrimitive.GuardStance => CalculateGuardStancePhysics(progress),
            _ => PhysicsOffset.Identity
        };
    }

    private static PhysicsOffset CalculateBouncePhysics(double progress)
    {
        // Phase 1 (0.0 to 0.15): Anticipation (Squash down)
        if (progress < 0.15)
        {
            var p = progress / 0.15;
            var easeP = Math.Sin(Math.PI * 0.5 * p);
            var squashY = 1.0 - (0.08 * easeP);
            var stretchX = 1.0 + (0.04 * easeP);
            var offsetY = 2.0 * easeP;
            return new PhysicsOffset(0.0, offsetY, stretchX, squashY);
        }

        // Phase 2 (0.15 to 0.75): Upward flight & peak stretch
        if (progress < 0.75)
        {
            var p = (progress - 0.15) / 0.60;
            var arc = Math.Sin(Math.PI * p);
            var offsetY = -14.0 * arc;
            var stretchY = 1.0 + (0.06 * arc);
            var squashX = 1.0 - (0.03 * arc);
            return new PhysicsOffset(0.0, offsetY, squashX, stretchY);
        }

        // Phase 3 (0.75 to 1.0): Landing follow-through & damped settle
        {
            var p = (progress - 0.75) / 0.25;
            var settle = Math.Sin(Math.PI * p) * Math.Exp(-3.5 * p);
            var squashY = 1.0 - (0.05 * settle);
            var stretchX = 1.0 + (0.03 * settle);
            var offsetY = 2.0 * settle;
            return new PhysicsOffset(0.0, offsetY, stretchX, squashY);
        }
    }

    private static PhysicsOffset CalculateWeightShiftPhysics(double progress)
    {
        // Periodic lateral rocking with smooth damping toward end
        var damping = 1.0 - (0.3 * progress);
        var offsetX = 3.0 * Math.Sin(2.0 * Math.PI * progress) * damping;
        var scaleY = 1.0 - (0.02 * Math.Abs(Math.Sin(2.0 * Math.PI * progress)) * damping);
        return new PhysicsOffset(offsetX, 0.0, 1.0, scaleY);
    }

    private static PhysicsOffset CalculateStretchPhysics(double progress)
    {
        var stretchProgress = Math.Sin(Math.PI * progress);
        var scaleY = 1.0 + (0.08 * stretchProgress);
        var scaleX = 1.0 - (0.04 * stretchProgress);
        var offsetY = -3.0 * stretchProgress;
        return new PhysicsOffset(0.0, offsetY, scaleX, scaleY);
    }

    private static PhysicsOffset CalculateHeadTiltPhysics(double progress, double direction)
    {
        var tilt = Math.Sin(Math.PI * progress);
        var offsetX = direction * 2.5 * tilt;
        var scaleY = 1.0 - (0.01 * tilt);
        return new PhysicsOffset(offsetX, 0.0, 1.0, scaleY);
    }

    private static PhysicsOffset CalculateLeanForwardPhysics(double progress)
    {
        var lean = Math.Sin(Math.PI * progress);
        var offsetX = 3.5 * lean;
        var offsetY = 1.0 * lean;
        return new PhysicsOffset(offsetX, offsetY, 1.02, 0.98);
    }

    private static PhysicsOffset CalculateStepBackPhysics(double progress)
    {
        var step = Math.Sin(Math.PI * progress);
        return new PhysicsOffset(-3.5 * step, 0.0, 0.98, 1.0);
    }

    private static PhysicsOffset CalculatePawStepPhysics(double progress)
    {
        var step = Math.Sin(2 * Math.PI * progress);
        var lift = -2.0 * Math.Abs(step);
        return new PhysicsOffset(1.5 * step, lift, 1.0, 1.0);
    }

    private static PhysicsOffset CalculateTailWagPhysics(double progress)
    {
        var wag = Math.Sin(4 * Math.PI * progress) * (1.0 - 0.2 * progress);
        return new PhysicsOffset(1.0 * wag, 0.0, 1.0, 1.0);
    }

    private static PhysicsOffset CalculateFistPumpPhysics(double progress)
    {
        // Sharp rise, gentle hold, damped return
        var pump = Math.Sin(Math.PI * progress);
        var offsetY = -8.0 * pump;
        return new PhysicsOffset(0.0, offsetY, 1.02, 1.04);
    }

    private static PhysicsOffset CalculateGuardStancePhysics(double progress)
    {
        var settle = Math.Sin(Math.PI * progress);
        var offsetY = 1.5 * settle;
        var scaleY = 1.0 - (0.03 * settle);
        var scaleX = 1.0 + (0.03 * settle);
        return new PhysicsOffset(0.0, offsetY, scaleX, scaleY);
    }
}
