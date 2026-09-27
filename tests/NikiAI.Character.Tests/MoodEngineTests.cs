using NikiAI.Character;
using NikiAI.Core.Character;
using Xunit;

namespace NikiAI.Character.Tests;

public class MoodEngineTests
{
    [Fact]
    public void MoodEngine_InitializesToCalm()
    {
        var engine = new MoodEngine();
        Assert.Equal(PetMood.Calm, engine.CurrentMood);
    }

    [Fact]
    public void TransitionTo_UpdatesMoodAndFiresEvent()
    {
        var engine = new MoodEngine();
        PetMood? transitionedTo = null;

        engine.MoodChanged += (s, result) =>
        {
            transitionedTo = result.NewMood;
        };

        var res = engine.TransitionTo(PetMood.Happy, "User pat");
        Assert.True(res.Changed);
        Assert.Equal(PetMood.Happy, res.NewMood);
        Assert.Equal(PetMood.Happy, engine.CurrentMood);
        Assert.Equal(PetMood.Happy, transitionedTo);
    }

    [Fact]
    public void ResetToCalm_ReturnsDirectlyToCalm()
    {
        var engine = new MoodEngine();
        engine.TransitionTo(PetMood.Excited, "Test");
        Assert.Equal(PetMood.Excited, engine.CurrentMood);

        engine.ResetToCalm();
        Assert.Equal(PetMood.Calm, engine.CurrentMood);
    }
}
