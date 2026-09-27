using NikiAI.Character;
using NikiAI.Core.Character;
using Xunit;

namespace NikiAI.Character.Tests;

public class LocalBehaviorEngineTests
{
    private readonly CharacterRegistry _registry = new();
    private readonly LocalBehaviorEngine _engine = new();

    [Theory]
    [InlineData("TaskStarted", PetMood.Focused, BehaviorPriority.TaskAttention)]
    [InlineData("TaskCompleted", PetMood.Happy, BehaviorPriority.ContextReaction)]
    [InlineData("TaskFailed", PetMood.Disappointed, BehaviorPriority.TaskAttention)]
    [InlineData("ApprovalRequired", PetMood.Surprised, BehaviorPriority.TaskAttention)]
    [InlineData("UserInteraction", PetMood.Playful, BehaviorPriority.UserInteraction)]
    [InlineData("VoiceListening", PetMood.Curious, BehaviorPriority.Command)]
    [InlineData("VoiceThinking", PetMood.Focused, BehaviorPriority.Command)]
    [InlineData("VoiceSpeaking", PetMood.Happy, BehaviorPriority.Command)]
    public void GeneratePlan_ContextualTriggers_GeneratesValidOfflinePlan(string trigger, PetMood expectedMood, BehaviorPriority expectedPriority)
    {
        var niki = _registry.GetIdentityProfile("niki")!;
        var context = new PetContextSnapshot(
            null,
            0,
            true,
            TimeSpan.FromMinutes(10),
            TimeOfDayBucket.Afternoon,
            DateTimeOffset.UtcNow);

        var plan = _engine.GeneratePlan(context, niki, trigger);

        Assert.NotNull(plan);
        Assert.Equal(expectedMood, plan.TargetMood);
        Assert.Equal(expectedPriority, plan.Priority);
        Assert.NotEmpty(plan.Sequence.Steps);
        Assert.True(plan.Sequence.TotalDuration > TimeSpan.Zero);
    }

    [Fact]
    public void GenerateSpontaneousPlan_EnforcesMinimum45SecondCooldown()
    {
        var dog = _registry.GetIdentityProfile("dog")!;
        var plan = _engine.GenerateSpontaneousPlan(dog);

        Assert.NotNull(plan);
        Assert.Equal(BehaviorPriority.Spontaneous, plan.Priority);
        Assert.True(plan.Cooldown >= TimeSpan.FromSeconds(45), "Spontaneous plans must have at least 45s cooldown");
    }
}
