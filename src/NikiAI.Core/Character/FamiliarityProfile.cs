namespace NikiAI.Core.Character;

/// <summary>
/// Low-risk, non-sensitive familiarity and interaction statistics for personality adaptation.
/// Strictly populated and evaluated only when PersonalityAdaptation setting is enabled.
/// </summary>
public record FamiliarityProfile(
    string CharacterId,
    int InteractionCount = 0,
    int TotalFocusSessionMinutes = 0,
    int PositiveReactionCount = 0,
    DateTime? LastInteraction = null);
