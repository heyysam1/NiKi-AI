namespace NikiAI.Core.Character;

/// <summary>
/// Authoritative identity, anatomical capabilities, and behavioral profile for a Desktop Pet character.
/// </summary>
public class CharacterIdentityProfile
{
    public string CharacterId { get; init; }
    public string DisplayName { get; init; }
    public string Species { get; init; }
    public string PersonalityDescription { get; init; }
    public CharacterCapability Capabilities { get; init; }
    public ExpressionTheme ExpressionTheme { get; init; }
    public MotionPrimitive SignatureReactionPrimitive { get; init; }
    public string SignatureDescription { get; init; }
    public IReadOnlyList<PetMood> NaturalEmotionalRange { get; init; }

    public CharacterIdentityProfile(
        string characterId,
        string displayName,
        string species,
        string personalityDescription,
        CharacterCapability capabilities,
        ExpressionTheme expressionTheme,
        MotionPrimitive signatureReactionPrimitive,
        string signatureDescription,
        IEnumerable<PetMood> naturalEmotionalRange)
    {
        CharacterId = characterId ?? throw new ArgumentNullException(nameof(characterId));
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        Species = species ?? string.Empty;
        PersonalityDescription = personalityDescription ?? string.Empty;
        Capabilities = capabilities;
        ExpressionTheme = expressionTheme;
        SignatureReactionPrimitive = signatureReactionPrimitive;
        SignatureDescription = signatureDescription ?? string.Empty;
        NaturalEmotionalRange = naturalEmotionalRange?.ToList().AsReadOnly() ?? new List<PetMood>().AsReadOnly();
    }

    /// <summary>
    /// Checks if this character has the specified anatomical capability.
    /// </summary>
    public bool HasCapability(CharacterCapability capability) => (Capabilities & capability) == capability;
}
