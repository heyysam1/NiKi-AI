using System;
using System.Threading;
using NikiAI.Character;
using NikiAI.Core.Character;
using Xunit;

namespace NikiAI.Character.Tests;

public class CasualIdleControllerTests
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
    public void Controller_Initializes_NotThrottledAndReducedMotionFalse()
    {
        RunInSta(() =>
        {
            var sm = new CharacterStateMachine();
            var registry = new CharacterRegistry();
            using var animController = new CharacterAnimationController(sm, registry);
            var clock = new FakeClock();
            var random = new FakeRandom(10);
            using var idleController = new CasualIdleController(sm, animController, clock, random);

            Assert.False(idleController.IsThrottled);
            Assert.False(idleController.ReducedMotion);
            Assert.NotNull(idleController.Behavior);
        });
    }

    [Fact]
    public void Controller_SetThrottled_UpdatesStateAndHaltsDispatch()
    {
        RunInSta(() =>
        {
            var sm = new CharacterStateMachine();
            var registry = new CharacterRegistry();
            using var animController = new CharacterAnimationController(sm, registry);
            var clock = new FakeClock();
            var random = new FakeRandom(10, 0);
            using var idleController = new CasualIdleController(sm, animController, clock, random);

            idleController.SetThrottled(true);
            Assert.True(idleController.IsThrottled);

            // Even when time advances past due time, throttled controller yields no action
            clock.UtcNow = clock.UtcNow.AddSeconds(15);
            var action = idleController.EvaluateAndDispatch();
            Assert.Null(action);

            idleController.SetThrottled(false);
            Assert.False(idleController.IsThrottled);
        });
    }

    [Fact]
    public void Controller_ReducedMotion_SuppressesFidgetDispatch()
    {
        RunInSta(() =>
        {
            var sm = new CharacterStateMachine();
            var registry = new CharacterRegistry();
            using var animController = new CharacterAnimationController(sm, registry);
            var clock = new FakeClock();
            var random = new FakeRandom(10, 0);
            using var idleController = new CasualIdleController(sm, animController, clock, random);

            idleController.ReducedMotion = true;
            Assert.True(idleController.ReducedMotion);

            clock.UtcNow = clock.UtcNow.AddSeconds(15);
            var action = idleController.EvaluateAndDispatch();
            Assert.Null(action);
        });
    }

    [Fact]
    public void Controller_RecordUserInteraction_ResetsActivity()
    {
        RunInSta(() =>
        {
            var sm = new CharacterStateMachine();
            var registry = new CharacterRegistry();
            using var animController = new CharacterAnimationController(sm, registry);
            var clock = new FakeClock();
            var random = new FakeRandom(10, 0);
            using var idleController = new CasualIdleController(sm, animController, clock, random);

            clock.UtcNow = clock.UtcNow.AddSeconds(5);
            idleController.RecordUserInteraction();

            Assert.Equal(clock.UtcNow, idleController.Behavior.LastActivityTime);
        });
    }

    private static void RunInSta(Action action)
    {
        Exception? ex = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                ex = e;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (ex != null)
        {
            throw new AggregateException("STA thread execution failed", ex);
        }
    }
}
