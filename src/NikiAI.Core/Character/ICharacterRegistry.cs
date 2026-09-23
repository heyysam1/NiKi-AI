namespace NikiAI.Core.Character;

/// <summary>
/// Registry contract for discovering, querying, and selecting active character profiles and their anatomical capabilities.
/// Acts as the single authoritative source of truth for character runtime and identity.
/// </summary>
public interface ICharacterRegistry
{
    IReadOnlyList<CharacterProfile> GetAllCharacters();
    CharacterProfile ActiveCharacter { get; }
    CharacterProfile? GetCharacter(string id);
    void SetActiveCharacter(string id);
    event EventHandler<CharacterProfile>? ActiveCharacterChanged;

    /// <summary>
    /// Gets the authoritative identity and capability profile for the active character.
    /// </summary>
    CharacterIdentityProfile ActiveIdentityProfile { get; }

    /// <summary>
    /// Gets the identity profile for a character by ID.
    /// </summary>
    CharacterIdentityProfile? GetIdentityProfile(string id);

    /// <summary>
    /// Gets all registered identity profiles.
    /// </summary>
    IReadOnlyList<CharacterIdentityProfile> GetAllIdentityProfiles();

    /// <summary>
    /// Checks whether the specified character is backed by valid, loadable on-disk animation assets.
    /// </summary>
    bool IsAssetBacked(string id);

    /// <summary>
    /// Reloads character animation manifests from disk without losing or redefining authoritative identity profiles.
    /// </summary>
    int ReloadCharacters(string? customAssetsPath = null);
}
