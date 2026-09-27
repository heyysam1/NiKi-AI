using NikiAI.Character;
using NikiAI.Core.Character;
using Xunit;

namespace NikiAI.Character.Tests;

public class ExpressionComposerTests
{
    private readonly CharacterRegistry _registry = new();
    private readonly ExpressionComposer _composer = new();

    [Fact]
    public void ComposeExpression_StandardAnime_CreatesValidRenderable()
    {
        var niki = _registry.GetIdentityProfile("niki")!;
        var intent = new ExpressionIntent(ExpressionTheme.StandardAnime, ExpressionSymbol.Sparkle, ExpressionIntensity.Medium, TimeSpan.FromSeconds(1));

        var renderable = _composer.ComposeExpression(intent, niki, false);
        Assert.NotNull(renderable);
        Assert.Equal(ExpressionSymbol.Sparkle, renderable.Symbol);
        Assert.Equal(ExpressionIntensity.Medium, renderable.Intensity);
    }

    [Fact]
    public void ComposeExpression_ReducedMotion_DampensIntensityAndOffsets()
    {
        var dog = _registry.GetIdentityProfile("dog")!;
        var intent = new ExpressionIntent(ExpressionTheme.Energetic, ExpressionSymbol.Heart, ExpressionIntensity.High, TimeSpan.FromSeconds(1));

        var renderable = _composer.ComposeExpression(intent, dog, true);
        Assert.NotNull(renderable);
        Assert.Equal(ExpressionIntensity.Low, renderable.Intensity);
        Assert.Equal(0, renderable.OffsetX);
        Assert.Equal(-6.0, renderable.OffsetY);
        Assert.True(renderable.ReducedMotion);
    }

    [Fact]
    public void ComposeExpression_ThemeMappings_MapSymbolsAccurately()
    {
        var gothGirl = _registry.GetIdentityProfile("character-06-goth-girl")!;
        var sparkleIntent = new ExpressionIntent(ExpressionTheme.DarkReserved, ExpressionSymbol.Sparkle, ExpressionIntensity.Medium, TimeSpan.FromSeconds(1));
        var gothRenderable = _composer.ComposeExpression(sparkleIntent, gothGirl, false);
        Assert.NotNull(gothRenderable);
        Assert.Equal(ExpressionSymbol.DarkOrb, gothRenderable.Symbol);

        var knight = _registry.GetIdentityProfile("character-04-knight")!;
        var heartIntent = new ExpressionIntent(ExpressionTheme.Heraldic, ExpressionSymbol.Heart, ExpressionIntensity.Medium, TimeSpan.FromSeconds(1));
        var knightRenderable = _composer.ComposeExpression(heartIntent, knight, false);
        Assert.NotNull(knightRenderable);
        Assert.Equal(ExpressionSymbol.HeraldicMark, knightRenderable.Symbol);
    }

    [Fact]
    public void ComposeExpression_DurationClamping_EnforcesBounds()
    {
        var niki = _registry.GetIdentityProfile("niki")!;

        // Below min bound (0.1s -> clamped to 0.5s)
        var shortIntent = new ExpressionIntent(ExpressionTheme.StandardAnime, ExpressionSymbol.Sparkle, ExpressionIntensity.Medium, TimeSpan.FromSeconds(0.1));
        var shortRenderable = _composer.ComposeExpression(shortIntent, niki, false);
        Assert.Equal(TimeSpan.FromSeconds(0.5), shortRenderable!.Duration);

        // Above max bound (5.0s -> clamped to 2.5s)
        var longIntent = new ExpressionIntent(ExpressionTheme.StandardAnime, ExpressionSymbol.Sparkle, ExpressionIntensity.Medium, TimeSpan.FromSeconds(5.0));
        var longRenderable = _composer.ComposeExpression(longIntent, niki, false);
        Assert.Equal(TimeSpan.FromSeconds(2.5), longRenderable!.Duration);

        // Under reduced motion: clamped to 1.0s max
        var reducedRenderable = _composer.ComposeExpression(longIntent, niki, true);
        Assert.Equal(TimeSpan.FromSeconds(1.0), reducedRenderable!.Duration);
    }

    [Fact]
    public void ComposeExpression_ZeroTextBanners_PureSymbolicRepresentation()
    {
        var knight = _registry.GetIdentityProfile("character-04-knight")!;
        var intent = new ExpressionIntent(ExpressionTheme.Heraldic, ExpressionSymbol.HeraldicMark, ExpressionIntensity.Medium, TimeSpan.FromSeconds(1));

        var renderable = _composer.ComposeExpression(intent, knight, false);
        Assert.NotNull(renderable);
        // RenderableExpression has only symbols, themes, and coordinates - no string text banners or task titles
        Assert.Equal(ExpressionSymbol.HeraldicMark, renderable.Symbol);
    }
}
