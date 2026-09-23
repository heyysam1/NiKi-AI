using Microsoft.Extensions.Logging;
using NikiAI.Core.Character;

namespace NikiAI.Character;

/// <summary>
/// Composes character-native visual expressions according to character profile theme and intensity.
/// Strictly adheres to visual-only expressions with zero text banners or task titles.
/// </summary>
public class ExpressionComposer : IExpressionComposer
{
    private readonly ILogger<ExpressionComposer>? _logger;

    public ExpressionComposer(ILogger<ExpressionComposer>? logger = null)
    {
        _logger = logger;
    }

    public RenderableExpression? ComposeExpression(
        ExpressionIntent intent,
        CharacterIdentityProfile profile,
        bool reducedMotion)
    {
        if (intent == null) return null;
        if (profile == null) throw new ArgumentNullException(nameof(profile));

        // Use character's native theme if intent theme does not match or is generic
        var effectiveTheme = profile.ExpressionTheme;

        // Choose symbol consistent with character identity
        var effectiveSymbol = MapSymbolToTheme(intent.Symbol, effectiveTheme);

        var duration = intent.Duration;
        if (duration < TimeSpan.FromSeconds(0.5))
        {
            duration = TimeSpan.FromSeconds(0.5);
        }
        else if (duration > TimeSpan.FromSeconds(2.5))
        {
            duration = TimeSpan.FromSeconds(2.5);
        }

        var intensity = intent.Intensity;
        var offsetY = -18.0;

        // Reduced Motion handling: clamp duration, lower intensity, lock vertical drift
        if (reducedMotion)
        {
            if (duration > TimeSpan.FromSeconds(1.0))
            {
                duration = TimeSpan.FromSeconds(1.0);
            }
            intensity = ExpressionIntensity.Low;
            offsetY = -6.0;
        }

        _logger?.LogDebug("Composed expression {Symbol} (Theme: {Theme}, Intensity: {Intensity}, ReducedMotion: {ReducedMotion})",
            effectiveSymbol, effectiveTheme, intensity, reducedMotion);

        return new RenderableExpression(
            effectiveTheme,
            effectiveSymbol,
            intensity,
            duration,
            OffsetX: 0.0,
            OffsetY: offsetY,
            ReducedMotion: reducedMotion);
    }

    private static ExpressionSymbol MapSymbolToTheme(ExpressionSymbol symbol, ExpressionTheme theme)
    {
        return (theme, symbol) switch
        {
            // Dark Reserved (Goth Girl): map sparkles, bursts, and hearts to dark orbs
            (ExpressionTheme.DarkReserved, ExpressionSymbol.Sparkle or ExpressionSymbol.StarBurst or ExpressionSymbol.Heart) =>
                ExpressionSymbol.DarkOrb,

            // Heraldic (Knight): map sparkles, bursts, and hearts to heraldic marks
            (ExpressionTheme.Heraldic, ExpressionSymbol.Sparkle or ExpressionSymbol.Heart or ExpressionSymbol.StarBurst) =>
                ExpressionSymbol.HeraldicMark,

            // Celestial Quiet: map star bursts / loud symbols to quiet focus spark
            (ExpressionTheme.CelestialQuiet, ExpressionSymbol.StarBurst or ExpressionSymbol.Exclamation) =>
                ExpressionSymbol.FocusSpark,

            // Standard Anime (Niki): map star bursts to subtle focus sparks
            (ExpressionTheme.StandardAnime, ExpressionSymbol.StarBurst) =>
                ExpressionSymbol.FocusSpark,

            // Casual Relaxed: map star burst to musical note
            (ExpressionTheme.CasualRelaxed, ExpressionSymbol.StarBurst) =>
                ExpressionSymbol.MusicalNote,

            // Feline: map heraldic mark or dark orb to native heart
            (ExpressionTheme.Feline, ExpressionSymbol.HeraldicMark or ExpressionSymbol.DarkOrb) =>
                ExpressionSymbol.Heart,

            _ => symbol
        };
    }
}
