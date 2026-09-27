using System;
using System.Collections.Generic;
using System.Threading;
using NikiAI.Character;
using NikiAI.Core.Character;
using Xunit;

namespace NikiAI.Character.Tests;

public class CharacterAnimationControllerTests
{
    private static CharacterProfile CreateMockProfile(string id, string name)
    {
        var animations = new Dictionary<CharacterState, CharacterAnimation>
        {
            [CharacterState.Idle] = new(CharacterState.Idle, new[]
            {
                new SpriteFrame("idle_0.png", 200),
                new SpriteFrame("idle_1.png", 200)
            }),
            [CharacterState.Working] = new(CharacterState.Working, new[]
            {
                new SpriteFrame("work_0.png", 150),
                new SpriteFrame("work_1.png", 150)
            }),
            [CharacterState.Happy] = new(CharacterState.Happy, new[]
            {
                new SpriteFrame("happy_0.png", 100)
            })
        };

        return new CharacterProfile(id, name, "Species", "Desc", animations);
    }

    [Fact]
    public void Controller_InitializesWithActiveCharacterAndIdleState()
    {
        RunInSta(() =>
        {
            var sm = new CharacterStateMachine();
            var registry = new CharacterRegistry();
            using var controller = new CharacterAnimationController(sm, registry);

            Assert.Equal("niki", controller.ActiveCharacter.Id, ignoreCase: true);
            Assert.Equal(CharacterState.Idle, controller.CurrentState);
            Assert.False(controller.IsThrottled);
            Assert.False(controller.ReducedMotion);
        });
    }

    [Fact]
    public void Controller_RespondsToStateChanges()
    {
        RunInSta(() =>
        {
            var sm = new CharacterStateMachine();
            var registry = new CharacterRegistry();
            using var controller = new CharacterAnimationController(sm, registry);

            var stateFired = false;
            CharacterState? newStateReceived = null;

            controller.AnimationStateChanged += (s, state) =>
            {
                stateFired = true;
                newStateReceived = state;
            };

            sm.SetState(CharacterState.Working);

            Assert.True(stateFired);
            Assert.Equal(CharacterState.Working, newStateReceived);
            Assert.Equal(CharacterState.Working, controller.CurrentState);
        });
    }

    [Fact]
    public void Controller_Throttling_CanBeToggled()
    {
        RunInSta(() =>
        {
            var sm = new CharacterStateMachine();
            var registry = new CharacterRegistry();
            using var controller = new CharacterAnimationController(sm, registry);

            Assert.False(controller.IsThrottled);

            controller.SetThrottled(true);
            Assert.True(controller.IsThrottled);

            controller.SetThrottled(false);
            Assert.False(controller.IsThrottled);
        });
    }

    [Fact]
    public void Controller_ReducedMotion_ClampsToFrameZero()
    {
        RunInSta(() =>
        {
            var sm = new CharacterStateMachine();
            var registry = new CharacterRegistry();
            using var controller = new CharacterAnimationController(sm, registry);

            controller.ReducedMotion = true;
            Assert.True(controller.ReducedMotion);
            Assert.Equal(0, controller.CurrentFrameIndex);
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
