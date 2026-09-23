namespace NikiAI.Core.Character;

/// <summary>
/// Abstraction for time retrieval to enable deterministic, non-flaky testing.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>
/// Abstraction for random number generation to enable deterministic, non-flaky testing.
/// </summary>
public interface IRandomSource
{
    int Next(int minValue, int maxValue);
}

public class SystemRandomSource : IRandomSource
{
    private readonly Random _random = new();
    public int Next(int minValue, int maxValue) => _random.Next(minValue, maxValue);
}

/// <summary>
/// Represents a purposeful casual idle fidget reaction.
/// </summary>
public record CasualFidgetAction(CharacterState State, TimeSpan Duration, string Reason);

/// <summary>
/// Pure domain logic managing casual idle variations.
/// Ensures the character does not continuously repeat the exact same static pose,
/// while respecting throttling, reduced motion, and user activity.
/// </summary>
public class CasualIdleBehavior
{
    private readonly IClock _clock;
    private readonly IRandomSource _random;

    public TimeSpan MinInterval { get; set; } = TimeSpan.FromSeconds(8);
    public TimeSpan MaxInterval { get; set; } = TimeSpan.FromSeconds(18);
    public TimeSpan DefaultFidgetDuration { get; set; } = TimeSpan.FromSeconds(1.5);

    public DateTimeOffset LastActivityTime { get; private set; }
    public DateTimeOffset NextFidgetDueTime { get; private set; }
    public CasualFidgetAction? ActiveFidget { get; private set; }
    public DateTimeOffset? FidgetEndTime { get; private set; }

    public CasualIdleBehavior(IClock? clock = null, IRandomSource? random = null)
    {
        _clock = clock ?? new SystemClock();
        _random = random ?? new SystemRandomSource();

        LastActivityTime = _clock.UtcNow;
        ScheduleNextFidget(LastActivityTime);
    }

    /// <summary>
    /// Resets the idle timer when user interaction occurs (mouse hover, click, drag, or command).
    /// </summary>
    public void RecordActivity(DateTimeOffset? now = null)
    {
        var timestamp = now ?? _clock.UtcNow;
        LastActivityTime = timestamp;
        ActiveFidget = null;
        FidgetEndTime = null;
        ScheduleNextFidget(timestamp);
    }

    /// <summary>
    /// Evaluates whether a casual fidget should begin or end based on the current state.
    /// </summary>
    public CasualFidgetAction? Evaluate(
        DateTimeOffset now,
        CharacterState currentState,
        bool isThrottled,
        bool reducedMotion)
    {
        // 1. Accessibility & Throttling: Suppress fidgets entirely if reduced motion is enabled or window is throttled
        if (isThrottled || reducedMotion)
        {
            if (ActiveFidget != null)
            {
                ActiveFidget = null;
                FidgetEndTime = null;
            }
            return null;
        }

        // 2. If a fidget is actively playing, check if its duration has completed
        if (ActiveFidget != null)
        {
            if (now >= FidgetEndTime)
            {
                ActiveFidget = null;
                FidgetEndTime = null;
                ScheduleNextFidget(now);
                return new CasualFidgetAction(CharacterState.Idle, TimeSpan.Zero, "Fidget completed, returning to base Idle");
            }
            return null; // Keep playing active fidget
        }

        // 3. Fidgets only trigger when the character is in base Idle state
        if (currentState != CharacterState.Idle)
        {
            LastActivityTime = now;
            ScheduleNextFidget(now);
            return null;
        }

        // 4. Check if the scheduled idle interval has elapsed
        if (now >= NextFidgetDueTime)
        {
            var fidget = PickNextFidget();
            ActiveFidget = fidget;
            FidgetEndTime = now + fidget.Duration;
            return fidget;
        }

        return null;
    }

    private void ScheduleNextFidget(DateTimeOffset fromTime)
    {
        int minSec = (int)MinInterval.TotalSeconds;
        int maxSec = (int)MaxInterval.TotalSeconds;
        int delaySec = _random.Next(minSec, Math.Max(minSec + 1, maxSec + 1));
        NextFidgetDueTime = fromTime.AddSeconds(delaySec);
    }

    private CasualFidgetAction PickNextFidget()
    {
        // Select varied lightweight casual behaviors
        int choice = _random.Next(0, 3);
        return choice switch
        {
            0 => new CasualFidgetAction(CharacterState.Listening, DefaultFidgetDuration, "Casual attentive glance"),
            1 => new CasualFidgetAction(CharacterState.Happy, DefaultFidgetDuration, "Casual subtle smile/perk"),
            _ => new CasualFidgetAction(CharacterState.Thinking, DefaultFidgetDuration, "Casual pondering glance")
        };
    }
}
