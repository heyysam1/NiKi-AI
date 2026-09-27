using System;
using System.Threading.Tasks;
using NikiAI.Character;
using NikiAI.Core.Character;
using Xunit;

namespace NikiAI.Character.Tests;

public class CharacterStateMachineTests
{
    [Fact]
    public void InitialState_IsIdle()
    {
        var sm = new CharacterStateMachine();
        Assert.Equal(CharacterState.Idle, sm.CurrentState);
    }

    [Fact]
    public void TransitionTo_UpdatesState_AndFiresEvent()
    {
        var sm = new CharacterStateMachine();
        CharacterState newState = CharacterState.Sleep;
        var eventFired = false;

        sm.StateChanged += (sender, args) =>
        {
            eventFired = true;
            newState = args;
        };

        sm.TransitionTo(CharacterState.Working);

        Assert.True(eventFired);
        Assert.Equal(CharacterState.Working, newState);
        Assert.Equal(CharacterState.Working, sm.CurrentState);
    }

    [Fact]
    public void TransitionTo_SameState_DoesNotFireEvent()
    {
        var sm = new CharacterStateMachine();
        var eventCount = 0;
        sm.StateChanged += (s, e) => eventCount++;

        sm.TransitionTo(CharacterState.Idle); // Already Idle
        Assert.Equal(0, eventCount);
        Assert.Equal(CharacterState.Idle, sm.CurrentState);
    }

    [Fact]
    public async Task TriggerReaction_RevertsToOriginalState_AfterDuration()
    {
        var sm = new CharacterStateMachine();
        sm.TransitionTo(CharacterState.Working);

        // Trigger reaction for 80ms
        sm.TriggerReaction(CharacterState.Happy, TimeSpan.FromMilliseconds(80));

        Assert.Equal(CharacterState.Happy, sm.CurrentState);

        // Wait for reaction to elapse
        await Task.Delay(160);

        Assert.Equal(CharacterState.Working, sm.CurrentState);
    }

    [Fact]
    public async Task TriggerReaction_OverriddenBySecondReaction_RevertsToOriginalPersistentState()
    {
        var sm = new CharacterStateMachine();
        sm.TransitionTo(CharacterState.Thinking);

        sm.TriggerReaction(CharacterState.Happy, TimeSpan.FromMilliseconds(100));
        Assert.Equal(CharacterState.Happy, sm.CurrentState);

        // Quickly trigger notification reaction
        sm.TriggerReaction(CharacterState.Notification, TimeSpan.FromMilliseconds(60));
        Assert.Equal(CharacterState.Notification, sm.CurrentState);

        // Wait until all reactions complete
        await Task.Delay(150);

        // Should return to Thinking, not Happy
        Assert.Equal(CharacterState.Thinking, sm.CurrentState);
    }

    [Fact]
    public void Reset_RestoresIdleState()
    {
        var sm = new CharacterStateMachine();
        sm.TransitionTo(CharacterState.Busy);
        Assert.Equal(CharacterState.Busy, sm.CurrentState);

        sm.Reset();
        Assert.Equal(CharacterState.Idle, sm.CurrentState);
    }
}
