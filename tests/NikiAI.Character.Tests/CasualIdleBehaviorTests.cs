using NikiAI.Core.Character;
using Xunit;

namespace NikiAI.Character.Tests;

public class CasualIdleBehaviorTests
{
    private class FakeClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);
    }

    private class FakeRandom : IRandomSource
    {
        private readonly int[] _sequence;
        private int _index;

        public FakeRandom(params int[] sequence)
        {
            _sequence = sequence;
        }

        public int Next(int minValue, int maxValue)
        {
            if (_sequence.Length == 0) return minValue;
            var val = _sequence[_index % _sequence.Length];
            _index++;
            return Math.Clamp(val, minValue, Math.Max(minValue, maxValue - 1));
        }
    }

    [Fact]
    public void Constructor_InitializesActivityAndSchedulesNextFidget()
    {
        var clock = new FakeClock();
        var random = new FakeRandom(10); // 10 second delay
        var behavior = new CasualIdleBehavior(clock, random)
        {
            MinInterval = TimeSpan.FromSeconds(8),
            MaxInterval = TimeSpan.FromSeconds(18)
        };

        Assert.Equal(clock.UtcNow, behavior.LastActivityTime);
        Assert.True(behavior.NextFidgetDueTime > clock.UtcNow);
        Assert.Null(behavior.ActiveFidget);
    }

    [Fact]
    public void Evaluate_BeforeDueTime_ReturnsNull()
    {
        var clock = new FakeClock();
        var random = new FakeRandom(12, 0); // 12 seconds delay, choice 0 (Listening)
        var behavior = new CasualIdleBehavior(clock, random)
        {
            MinInterval = TimeSpan.FromSeconds(8),
            MaxInterval = TimeSpan.FromSeconds(18)
        };

        clock.UtcNow = clock.UtcNow.AddSeconds(5); // Not yet 12 seconds
        var action = behavior.Evaluate(clock.UtcNow, CharacterState.Idle, isThrottled: false, reducedMotion: false);

        Assert.Null(action);
        Assert.Null(behavior.ActiveFidget);
    }

    [Fact]
    public void Evaluate_WhenDueTimeReached_TriggersFidget()
    {
        var clock = new FakeClock();
        var random = new FakeRandom(10, 1); // 10s delay, choice 1 (Happy)
        var behavior = new CasualIdleBehavior(clock, random)
        {
            MinInterval = TimeSpan.FromSeconds(8),
            MaxInterval = TimeSpan.FromSeconds(18),
            DefaultFidgetDuration = TimeSpan.FromSeconds(1.5)
        };

        // Advance to due time
        clock.UtcNow = clock.UtcNow.AddSeconds(10);
        var action = behavior.Evaluate(clock.UtcNow, CharacterState.Idle, isThrottled: false, reducedMotion: false);

        Assert.NotNull(action);
        Assert.Equal(CharacterState.Happy, action!.State);
        Assert.Equal(TimeSpan.FromSeconds(1.5), action.Duration);
        Assert.NotNull(behavior.ActiveFidget);
    }

    [Fact]
    public void Evaluate_WhileFidgetActive_MaintainsUntilDurationExpiresThenReturnsToIdle()
    {
        var clock = new FakeClock();
        var random = new FakeRandom(10, 2); // 10s delay, choice 2 (Thinking)
        var behavior = new CasualIdleBehavior(clock, random)
        {
            MinInterval = TimeSpan.FromSeconds(8),
            MaxInterval = TimeSpan.FromSeconds(18),
            DefaultFidgetDuration = TimeSpan.FromSeconds(2.0)
        };

        // 1. Trigger fidget
        clock.UtcNow = clock.UtcNow.AddSeconds(10);
        var action1 = behavior.Evaluate(clock.UtcNow, CharacterState.Idle, isThrottled: false, reducedMotion: false);
        Assert.NotNull(action1);
        Assert.Equal(CharacterState.Thinking, action1!.State);

        // 2. Advance clock during fidget (1 second into 2s duration)
        clock.UtcNow = clock.UtcNow.AddSeconds(1);
        var action2 = behavior.Evaluate(clock.UtcNow, CharacterState.Idle, isThrottled: false, reducedMotion: false);
        Assert.Null(action2); // Continues active fidget

        // 3. Advance past end time (another 1.5 seconds)
        clock.UtcNow = clock.UtcNow.AddSeconds(1.5);
        var action3 = behavior.Evaluate(clock.UtcNow, CharacterState.Idle, isThrottled: false, reducedMotion: false);

        Assert.NotNull(action3);
        Assert.Equal(CharacterState.Idle, action3!.State);
        Assert.Null(behavior.ActiveFidget);
        Assert.True(behavior.NextFidgetDueTime > clock.UtcNow);
    }

    [Fact]
    public void Evaluate_WhenThrottled_SuppressesFidgetsAndClearsActive()
    {
        var clock = new FakeClock();
        var random = new FakeRandom(10, 0);
        var behavior = new CasualIdleBehavior(clock, random);

        clock.UtcNow = clock.UtcNow.AddSeconds(10);
        var action = behavior.Evaluate(clock.UtcNow, CharacterState.Idle, isThrottled: true, reducedMotion: false);

        Assert.Null(action);
        Assert.Null(behavior.ActiveFidget);
    }

    [Fact]
    public void Evaluate_WhenReducedMotionEnabled_SuppressesFidgets()
    {
        var clock = new FakeClock();
        var random = new FakeRandom(10, 0);
        var behavior = new CasualIdleBehavior(clock, random);

        clock.UtcNow = clock.UtcNow.AddSeconds(10);
        var action = behavior.Evaluate(clock.UtcNow, CharacterState.Idle, isThrottled: false, reducedMotion: true);

        Assert.Null(action);
        Assert.Null(behavior.ActiveFidget);
    }

    [Fact]
    public void Evaluate_WhenCharacterInActiveState_PostponesFidget()
    {
        var clock = new FakeClock();
        var random = new FakeRandom(10, 0);
        var behavior = new CasualIdleBehavior(clock, random);

        clock.UtcNow = clock.UtcNow.AddSeconds(10);
        // Character is Working on a background task
        var action = behavior.Evaluate(clock.UtcNow, CharacterState.Working, isThrottled: false, reducedMotion: false);

        Assert.Null(action);
        Assert.Null(behavior.ActiveFidget);
        Assert.True(behavior.NextFidgetDueTime > clock.UtcNow);
    }

    [Fact]
    public void RecordActivity_ResetsDueTimeAndCancelsActiveFidget()
    {
        var clock = new FakeClock();
        var random = new FakeRandom(10, 0);
        var behavior = new CasualIdleBehavior(clock, random);

        clock.UtcNow = clock.UtcNow.AddSeconds(10);
        behavior.Evaluate(clock.UtcNow, CharacterState.Idle, isThrottled: false, reducedMotion: false);
        Assert.NotNull(behavior.ActiveFidget);

        // User moves mouse / interacts
        behavior.RecordActivity(clock.UtcNow);

        Assert.Null(behavior.ActiveFidget);
        Assert.Equal(clock.UtcNow, behavior.LastActivityTime);
        Assert.True(behavior.NextFidgetDueTime > clock.UtcNow);
    }
}
