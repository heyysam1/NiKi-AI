namespace NikiAI.Core.Character;

/// <summary>
/// Priority levels for behavior execution scheduling.
/// Lower integer value corresponds to higher execution priority.
/// </summary>
public enum BehaviorPriority
{
    UserInteraction = 1,
    Command = 2,
    TaskAttention = 3,
    Notification = 4,
    ContextReaction = 5,
    Spontaneous = 6,
    Idle = 7
}

/// <summary>
/// Structured, validated behavior plan produced by the behavior director.
/// Strictly decoupled from privileged OS tools, permissions, or system state.
/// </summary>
public record BehaviorPlan(
    string PlanId,
    string Intent,
    PetMood TargetMood,
    MotionSequence Sequence,
    ExpressionIntent? Expression,
    BehaviorPriority Priority,
    TimeSpan Cooldown,
    bool IsInterruptible = true);
