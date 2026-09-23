namespace NikiAI.Core.Character;

/// <summary>
/// Temporary, lightweight mood states for Desktop Pet characters as defined in 12_DESKTOP_PET_AI_BEHAVIOR_DIRECTOR.md.
/// </summary>
public enum PetMood
{
    Calm,
    Curious,
    Happy,
    Excited,
    Tired,
    Focused,
    Surprised,
    Confused,
    Disappointed,
    Sleepy,
    Playful
}

/// <summary>
/// Result of a mood state transition.
/// </summary>
public record MoodTransitionResult(
    PetMood PreviousMood,
    PetMood NewMood,
    string Reason,
    DateTime Timestamp)
{
    public bool Changed => PreviousMood != NewMood;
}
