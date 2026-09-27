using NikiAI.Character;
using NikiAI.Core.Character;
using Xunit;

namespace NikiAI.Character.Tests;

public class BehaviorExecutionCoordinatorTests
{
    private class FakeCharacterStateMachine : ICharacterStateMachine
    {
        public CharacterState CurrentState { get; private set; } = CharacterState.Idle;

        public event EventHandler<CharacterState>? StateChanged;

        public void SetState(CharacterState newState)
        {
            CurrentState = newState;
            StateChanged?.Invoke(this, newState);
        }

        public void TriggerReaction(CharacterState reactionState, TimeSpan duration, CharacterState? returnState = null)
        {
            SetState(reactionState);
        }
    }

    [Fact]
    public async Task EnqueueOrExecute_ExecutesPlanAndReturnsToIdle()
    {
        var stateMachine = new FakeCharacterStateMachine();
        var registry = new CharacterRegistry();
        var validator = new CharacterCapabilityValidator();
        var composer = new ExpressionComposer();
        var physics = new MotionPhysicsSimulator();
        var mood = new MoodEngine();

        using var coordinator = new BehaviorExecutionCoordinator(stateMachine, registry, validator, composer, physics, mood);

        var plan = new BehaviorPlan(
            "p1",
            "TestGreeting",
            PetMood.Happy,
            new MotionSequence("ShortSeq", [new MotionPrimitiveStep(MotionPrimitive.Nod, TimeSpan.FromMilliseconds(50))]),
            null,
            BehaviorPriority.UserInteraction,
            TimeSpan.FromMilliseconds(50));

        var tcs = new TaskCompletionSource();
        coordinator.PlanCompleted += (s, p) => tcs.TrySetResult();

        var accepted = coordinator.EnqueueOrExecute(plan, false);
        Assert.True(accepted);

        await Task.WhenAny(tcs.Task, Task.Delay(2000));

        Assert.Equal(CharacterState.Idle, stateMachine.CurrentState);
        Assert.False(coordinator.IsExecuting);
    }

    [Fact]
    public void EnqueueOrExecute_SpontaneousCooldown_RejectsRapidExecution()
    {
        var stateMachine = new FakeCharacterStateMachine();
        var registry = new CharacterRegistry();
        var validator = new CharacterCapabilityValidator();
        var composer = new ExpressionComposer();
        var physics = new MotionPhysicsSimulator();
        var mood = new MoodEngine();

        using var coordinator = new BehaviorExecutionCoordinator(stateMachine, registry, validator, composer, physics, mood);

        var plan1 = new BehaviorPlan(
            "s1",
            "Spontaneous1",
            PetMood.Curious,
            new MotionSequence("Seq1", [new MotionPrimitiveStep(MotionPrimitive.LookLeft, TimeSpan.FromMilliseconds(50))]),
            null,
            BehaviorPriority.Spontaneous,
            TimeSpan.FromSeconds(45));

        var plan2 = new BehaviorPlan(
            "s2",
            "Spontaneous2",
            PetMood.Curious,
            new MotionSequence("Seq2", [new MotionPrimitiveStep(MotionPrimitive.LookRight, TimeSpan.FromMilliseconds(50))]),
            null,
            BehaviorPriority.Spontaneous,
            TimeSpan.FromSeconds(45));

        var accepted1 = coordinator.EnqueueOrExecute(plan1, false);
        Assert.True(accepted1);

        // Immediate second spontaneous plan must be rejected due to 45s cooldown
        var accepted2 = coordinator.EnqueueOrExecute(plan2, false);
        Assert.False(accepted2);
    }

    [Fact]
    public void Preemption_HigherPriorityPreemptsLowerPriority()
    {
        var stateMachine = new FakeCharacterStateMachine();
        var registry = new CharacterRegistry();
        var validator = new CharacterCapabilityValidator();
        var composer = new ExpressionComposer();
        var physics = new MotionPhysicsSimulator();
        var mood = new MoodEngine();

        using var coordinator = new BehaviorExecutionCoordinator(stateMachine, registry, validator, composer, physics, mood);

        var lowPriPlan = new BehaviorPlan(
            "low",
            "SpontaneousLow",
            PetMood.Calm,
            new MotionSequence("LongSeq", [new MotionPrimitiveStep(MotionPrimitive.Stretch, TimeSpan.FromSeconds(2))]),
            null,
            BehaviorPriority.Spontaneous,
            TimeSpan.FromSeconds(45),
            IsInterruptible: true);

        var highPriPlan = new BehaviorPlan(
            "high",
            "UserActionHigh",
            PetMood.Happy,
            new MotionSequence("ShortSeq", [new MotionPrimitiveStep(MotionPrimitive.Bounce, TimeSpan.FromMilliseconds(100))]),
            null,
            BehaviorPriority.UserInteraction,
            TimeSpan.FromSeconds(1));

        var lowAccepted = coordinator.EnqueueOrExecute(lowPriPlan, false);
        Assert.True(lowAccepted);

        // High priority plan submitted while low priority is running
        var highAccepted = coordinator.EnqueueOrExecute(highPriPlan, false);
        Assert.True(highAccepted);
        Assert.Equal("UserActionHigh", coordinator.ActivePlan?.Intent);
    }
}
