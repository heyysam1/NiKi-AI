namespace NikiAI.Core.Character;

/// <summary>
/// Composes character-native visual expressions from intent, taking into account character theme and reduced motion settings.
/// Strictly forbids text banners or task titles.
/// </summary>
public interface IExpressionComposer
{
    /// <summary>
    /// Composes a renderable visual expression from the given intent and character profile.
    /// Returns null if expressions are suppressed or invalid.
    /// </summary>
    RenderableExpression? ComposeExpression(ExpressionIntent intent, CharacterIdentityProfile profile, bool reducedMotion);
}
