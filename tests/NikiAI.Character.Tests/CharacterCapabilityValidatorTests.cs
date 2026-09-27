using NikiAI.Character;
using NikiAI.Core.Character;
using Xunit;

namespace NikiAI.Character.Tests;

public class CharacterCapabilityValidatorTests
{
    private readonly CharacterRegistry _registry = new();
    private readonly CharacterCapabilityValidator _validator = new();

    [Fact]
    public void ValidatePrimitive_KnightLackingTail_SubstitutesWeightShift()
    {
        var knight = _registry.GetIdentityProfile("character-04-knight")!;
        Assert.False(knight.HasCapability(CharacterCapability.Tail));

        var result = _validator.ValidatePrimitive(MotionPrimitive.TailWag, knight);
        Assert.NotEqual(MotionPrimitive.TailWag, result);
        Assert.Equal(MotionPrimitive.ShiftWeight, result);
    }

    [Fact]
    public void ValidatePrimitive_DogWithTail_RetainsTailWag()
    {
        var dog = _registry.GetIdentityProfile("dog")!;
        Assert.True(dog.HasCapability(CharacterCapability.Tail));

        var result = _validator.ValidatePrimitive(MotionPrimitive.TailWag, dog);
        Assert.Equal(MotionPrimitive.TailWag, result);
    }

    [Fact]
    public void ValidatePrimitive_KnightWithoutEars_SubstitutesHeadTiltRight()
    {
        var knight = _registry.GetIdentityProfile("character-04-knight")!;
        Assert.False(knight.HasCapability(CharacterCapability.CatEars));
        Assert.False(knight.HasCapability(CharacterCapability.FloppyEars));

        var result = _validator.ValidatePrimitive(MotionPrimitive.EarTwitch, knight);
        Assert.NotEqual(MotionPrimitive.EarTwitch, result);
        Assert.Equal(MotionPrimitive.HeadTiltRight, result);
    }

    [Fact]
    public void ValidateAndAdapt_PlanWithUnsupportedMood_FallsBackToCalm()
    {
        var niki = _registry.GetIdentityProfile("niki")!;
        var unsupportedMood = PetMood.Confused; // Not in Niki's emotional range: Calm, Focused, Happy, Tired

        var plan = new BehaviorPlan(
            "p1",
            "TestPlan",
            unsupportedMood,
            new MotionSequence("Seq", [new MotionPrimitiveStep(MotionPrimitive.Idle, TimeSpan.FromSeconds(1))]),
            null,
            BehaviorPriority.Spontaneous,
            TimeSpan.FromSeconds(45));

        var adapted = _validator.ValidateAndAdapt(plan, niki);
        Assert.Equal(PetMood.Calm, adapted.TargetMood);
    }

    [Fact]
    public void ValidateAndAdapt_EmptySteps_GuaranteesIdleStep()
    {
        var niki = _registry.GetIdentityProfile("niki")!;

        var plan = new BehaviorPlan(
            "p2",
            "EmptyPlan",
            PetMood.Calm,
            new MotionSequence("EmptySeq", Array.Empty<MotionPrimitiveStep>()),
            null,
            BehaviorPriority.Spontaneous,
            TimeSpan.FromSeconds(45));

        var adapted = _validator.ValidateAndAdapt(plan, niki);
        Assert.NotEmpty(adapted.Sequence.Steps);
        Assert.Equal(MotionPrimitive.Idle, adapted.Sequence.Steps[0].Primitive);
    }
}
