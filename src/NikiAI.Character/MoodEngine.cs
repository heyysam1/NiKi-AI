using Microsoft.Extensions.Logging;
using NikiAI.Core.Character;

namespace NikiAI.Character;

/// <summary>
/// Manages temporary, lightweight mood states for Desktop Pet characters as defined in 12_DESKTOP_PET_AI_BEHAVIOR_DIRECTOR.md.
/// Decays smoothly back to Calm during inactivity.
/// </summary>
public class MoodEngine
{
    private readonly ILogger<MoodEngine>? _logger;
    private readonly object _lock = new();

    public PetMood CurrentMood { get; private set; } = PetMood.Calm;
    public DateTime LastTransitionTime { get; private set; } = DateTime.UtcNow;

    public event EventHandler<MoodTransitionResult>? MoodChanged;

    public MoodEngine(ILogger<MoodEngine>? logger = null)
    {
        _logger = logger;
    }

    public MoodTransitionResult TransitionTo(PetMood newMood, string reason)
    {
        lock (_lock)
        {
            var previous = CurrentMood;
            CurrentMood = newMood;
            LastTransitionTime = DateTime.UtcNow;

            var result = new MoodTransitionResult(previous, newMood, reason, LastTransitionTime);

            if (previous != newMood)
            {
                _logger?.LogDebug("Pet mood changed: {Previous} -> {New} ({Reason})", previous, newMood, reason);
                MoodChanged?.Invoke(this, result);
            }

            return result;
        }
    }

    /// <summary>
    /// Evaluates current pet context and updates mood state accordingly.
    /// </summary>
    public MoodTransitionResult EvaluateContext(PetContextSnapshot context)
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            var timeSinceLastTransition = now - LastTransitionTime;

            // Long session without user interaction decays to Tired or Sleepy
            if (context.SessionDuration > TimeSpan.FromMinutes(60) && context.IsUserActive == false)
            {
                if (CurrentMood is not PetMood.Sleepy and not PetMood.Tired)
                {
                    return TransitionTo(PetMood.Sleepy, "Prolonged inactivity during extended session");
                }
            }
            else if (context.SessionDuration > TimeSpan.FromMinutes(45) && CurrentMood == PetMood.Calm)
            {
                return TransitionTo(PetMood.Tired, "Long work session");
            }

            // High energy moods (Excited, Surprised, Disappointed) decay back to Calm after 60 seconds
            if (timeSinceLastTransition > TimeSpan.FromSeconds(60))
            {
                if (CurrentMood is PetMood.Excited or PetMood.Surprised or PetMood.Disappointed or PetMood.Confused)
                {
                    return TransitionTo(PetMood.Calm, "Natural emotional decay to Calm baseline");
                }
            }

            return new MoodTransitionResult(CurrentMood, CurrentMood, "Unchanged", now);
        }
    }

    /// <summary>
    /// Resets mood to Calm.
    /// </summary>
    public void ResetToCalm()
    {
        TransitionTo(PetMood.Calm, "Reset");
    }
}
