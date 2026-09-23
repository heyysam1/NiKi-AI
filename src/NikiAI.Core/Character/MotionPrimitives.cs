namespace NikiAI.Core.Character;

/// <summary>
/// Atomic motion primitives decomposed from monolithic animations.
/// </summary>
public enum MotionPrimitive
{
    Idle,
    HeadTiltLeft,
    HeadTiltRight,
    LookLeft,
    LookRight,
    Blink,
    SlowBlink,
    Nod,
    Smile,
    Yawn,
    Stretch,
    Bounce,
    ShiftWeight,
    LeanForward,
    StepBack,
    PawStep,
    TailWag,
    TailCurl,
    EarTwitch,
    GuardStance,
    GearCheck,
    HairAdjust,
    JacketAdjust,
    CapAdjust,
    FistPump,
    Sit,
    LieDown
}

/// <summary>
/// A single atomic step in a composed motion sequence.
/// </summary>
public record MotionPrimitiveStep(
    MotionPrimitive Primitive,
    TimeSpan Duration,
    bool Interruptible = true,
    double Intensity = 1.0);

/// <summary>
/// An ordered sequence of motion primitive steps representing a full composed behavior.
/// </summary>
public record MotionSequence(
    string Name,
    IReadOnlyList<MotionPrimitiveStep> Steps)
{
    public TimeSpan TotalDuration => TimeSpan.FromMilliseconds(Steps?.Sum(s => s.Duration.TotalMilliseconds) ?? 0);
}
