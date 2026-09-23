using NikiAI.Core.Tasks;

namespace NikiAI.Core.Character;

/// <summary>
/// Broad time-of-day buckets for contextual non-sensitive awareness.
/// </summary>
public enum TimeOfDayBucket
{
    Morning,
    Afternoon,
    Evening,
    Night
}

/// <summary>
/// Ephemeral, read-only context snapshot consumed as a non-sensitive foundation for future behavior directors.
/// Strictly unpersisted, free of screen captures, OCR, or sensitive profiling.
/// </summary>
public record PetContextSnapshot(
    AgentTaskStatus? CurrentTaskState,
    int ActiveNotificationCount,
    bool IsUserActive,
    TimeSpan SessionDuration,
    TimeOfDayBucket TimeOfDay,
    DateTimeOffset? LastPetInteractionTime
);

/// <summary>
/// Ephemeral, event/state-based context provider for Desktop Pet runtime signals.
/// Operates independently of whether permanent memory is enabled or disabled.
/// </summary>
public interface IPetContextProvider
{
    /// <summary>
    /// Gets the current non-sensitive runtime context snapshot.
    /// </summary>
    PetContextSnapshot GetContextSnapshot();

    /// <summary>
    /// Records user interaction with the pet (such as clicks, drags, or petting).
    /// </summary>
    void RecordUserInteraction();
}
