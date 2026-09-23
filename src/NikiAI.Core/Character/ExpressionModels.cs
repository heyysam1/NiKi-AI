namespace NikiAI.Core.Character;

/// <summary>
/// Character-native expression themes aligned with character identities.
/// </summary>
public enum ExpressionTheme
{
    StandardAnime,
    Energetic,
    CelestialQuiet,
    Heraldic,
    CelestialEnergetic,
    DarkReserved,
    CasualRelaxed,
    Adventurous,
    Feline
}

/// <summary>
/// Graphical expression symbols rendered above the character sprite.
/// Never contains text, banners, or task titles.
/// </summary>
public enum ExpressionSymbol
{
    Sparkle,
    Heart,
    SweatDrop,
    Exclamation,
    Question,
    StarBurst,
    MusicalNote,
    HeraldicMark,
    DarkOrb,
    FocusSpark
}

/// <summary>
/// Visual expression intensity level.
/// </summary>
public enum ExpressionIntensity
{
    Low,
    Medium,
    High
}

/// <summary>
/// Structured intent to display a character-native graphical expression.
/// </summary>
public record ExpressionIntent(
    ExpressionTheme Theme,
    ExpressionSymbol Symbol,
    ExpressionIntensity Intensity,
    TimeSpan Duration);

/// <summary>
/// Renderable visual expression details for the UI layer overlay.
/// </summary>
public record RenderableExpression(
    ExpressionTheme Theme,
    ExpressionSymbol Symbol,
    ExpressionIntensity Intensity,
    TimeSpan Duration,
    double OffsetX = 0,
    double OffsetY = -15,
    bool ReducedMotion = false);
