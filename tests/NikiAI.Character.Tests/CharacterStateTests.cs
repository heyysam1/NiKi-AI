using System;
using System.Collections.Generic;
using NikiAI.Core.Character;
using NikiAI.Core.Tasks;
using Xunit;

namespace NikiAI.Character.Tests;

public class CharacterStateTests
{
    [Fact]
    public void CharacterState_ContainsAllFifteenRequiredStates()
    {
        var expectedStates = new[]
        {
            CharacterState.Idle,
            CharacterState.Walk,
            CharacterState.Run,
            CharacterState.Jump,
            CharacterState.Listening,
            CharacterState.Thinking,
            CharacterState.Working,
            CharacterState.Talking,
            CharacterState.Happy,
            CharacterState.Notification,
            CharacterState.Sleep,
            CharacterState.Error,
            CharacterState.WaitingForApproval,
            CharacterState.TaskComplete,
            CharacterState.Busy
        };

        var actualStates = Enum.GetValues<CharacterState>();
        Assert.Equal(15, actualStates.Length);

        foreach (var state in expectedStates)
        {
            Assert.Contains(state, actualStates);
        }
    }

    [Fact]
    public void CharacterProfile_ResolvesAllFifteenStatesWithoutNull()
    {
        var profile = CreateSampleProfile();

        foreach (CharacterState state in Enum.GetValues<CharacterState>())
        {
            var anim = profile.GetAnimation(state);
            Assert.NotNull(anim);
            Assert.NotEmpty(anim.Frames);
            Assert.True(anim.TotalDurationMs > 0);
        }
    }

    [Theory]
    [InlineData(CharacterState.Walk, CharacterState.Idle)]
    [InlineData(CharacterState.Run, CharacterState.Idle)]
    [InlineData(CharacterState.Jump, CharacterState.Happy)]
    [InlineData(CharacterState.Talking, CharacterState.Listening)]
    [InlineData(CharacterState.Error, CharacterState.Thinking)]
    [InlineData(CharacterState.WaitingForApproval, CharacterState.Thinking)]
    [InlineData(CharacterState.TaskComplete, CharacterState.Happy)]
    [InlineData(CharacterState.Busy, CharacterState.Working)]
    public void CharacterProfile_FallbackHierarchy_ResolvesToSensibleDefaults(
        CharacterState unauthoredState, CharacterState expectedFallback)
    {
        var profile = CreateSampleProfile();

        // The profile only explicitly authored Idle, Listening, Thinking, Working, Happy, Notification, Sleep
        var anim = profile.GetAnimation(unauthoredState);
        var expectedAnim = profile.GetAnimation(expectedFallback);

        Assert.NotNull(anim);
        Assert.Same(expectedAnim, anim);
    }

    [Fact]
    public void CharacterAnimation_GetFrameAtTime_LoopsProperly()
    {
        var frames = new List<SpriteFrame>
        {
            new("f0.png", 100),
            new("f1.png", 200),
            new("f2.png", 300)
        };
        var animation = new CharacterAnimation(CharacterState.Idle, frames, isLooping: true);

        Assert.Equal(600, animation.TotalDurationMs);

        // At 0ms -> frame 0
        Assert.Equal("f0.png", animation.GetFrameAtTime(0).AssetPath);
        // At 99ms -> frame 0
        Assert.Equal("f0.png", animation.GetFrameAtTime(99).AssetPath);
        // At 100ms -> frame 1
        Assert.Equal("f1.png", animation.GetFrameAtTime(100).AssetPath);
        // At 299ms -> frame 1
        Assert.Equal("f1.png", animation.GetFrameAtTime(299).AssetPath);
        // At 300ms -> frame 2
        Assert.Equal("f2.png", animation.GetFrameAtTime(300).AssetPath);
        // At 599ms -> frame 2
        Assert.Equal("f2.png", animation.GetFrameAtTime(599).AssetPath);
        // At 600ms (loops back) -> frame 0
        Assert.Equal("f0.png", animation.GetFrameAtTime(600).AssetPath);
        // At 750ms -> frame 1 (600 + 150)
        Assert.Equal("f1.png", animation.GetFrameAtTime(750).AssetPath);
    }

    [Fact]
    public void CharacterStateMapping_MapsTaskStatusCorrectly()
    {
        Assert.Equal(CharacterState.Working, CharacterStateMapping.FromTaskStatus(AgentTaskStatus.Running));
        Assert.Equal(CharacterState.Happy, CharacterStateMapping.FromTaskStatus(AgentTaskStatus.Completed));
        Assert.Equal(CharacterState.Error, CharacterStateMapping.FromTaskStatus(AgentTaskStatus.Failed));
        Assert.Equal(CharacterState.Thinking, CharacterStateMapping.FromTaskStatus(AgentTaskStatus.Pending));
        Assert.Equal(CharacterState.Listening, CharacterStateMapping.FromTaskStatus(AgentTaskStatus.Waiting));
        Assert.Equal(CharacterState.WaitingForApproval, CharacterStateMapping.FromTaskStatus(AgentTaskStatus.NeedsApproval));
        Assert.Equal(CharacterState.Idle, CharacterStateMapping.FromTaskStatus(AgentTaskStatus.Draft));
        Assert.Equal(CharacterState.Idle, CharacterStateMapping.FromTaskStatus(AgentTaskStatus.Cancelled));
    }

    private static CharacterProfile CreateSampleProfile()
    {
        var animations = new Dictionary<CharacterState, CharacterAnimation>
        {
            [CharacterState.Idle] = new(CharacterState.Idle, new[] { new SpriteFrame("idle.png", 300) }),
            [CharacterState.Listening] = new(CharacterState.Listening, new[] { new SpriteFrame("listen.png", 300) }),
            [CharacterState.Thinking] = new(CharacterState.Thinking, new[] { new SpriteFrame("think.png", 300) }),
            [CharacterState.Working] = new(CharacterState.Working, new[] { new SpriteFrame("work.png", 300) }),
            [CharacterState.Happy] = new(CharacterState.Happy, new[] { new SpriteFrame("happy.png", 300) }),
            [CharacterState.Notification] = new(CharacterState.Notification, new[] { new SpriteFrame("notif.png", 300) }),
            [CharacterState.Sleep] = new(CharacterState.Sleep, new[] { new SpriteFrame("sleep.png", 300) })
        };

        return new CharacterProfile("sample", "Sample Character", "Species", "Sample Description", animations);
    }
}
