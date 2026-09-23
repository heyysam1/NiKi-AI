namespace NikiAI.Core.Character;

/// <summary>
/// Defines a character's identity, metadata, and state animations.
/// Fully supports all 15 character states from the specification with architectural fallback.
/// </summary>
public class CharacterProfile
{
    public string Id { get; init; }
    public string DisplayName { get; init; }
    public string Species { get; init; }
    public string Description { get; init; }
    public IReadOnlyDictionary<CharacterState, CharacterAnimation> Animations { get; }

    public CharacterProfile(
        string id,
        string displayName,
        string species,
        string description,
        IDictionary<CharacterState, CharacterAnimation> animations)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        Species = species ?? string.Empty;
        Description = description ?? string.Empty;
        Animations = new Dictionary<CharacterState, CharacterAnimation>(animations);

        if (!Animations.ContainsKey(CharacterState.Idle))
        {
            throw new ArgumentException("CharacterProfile must at minimum define an animation for CharacterState.Idle.", nameof(animations));
        }
    }

    /// <summary>
    /// Retrieves the animation for the requested state.
    /// Supports the full 15-state specification model with graceful fallback to compatible base states.
    /// </summary>
    public CharacterAnimation GetAnimation(CharacterState state)
    {
        if (Animations.TryGetValue(state, out var anim))
        {
            return anim;
        }

        // Architectural fallback map as specified in 02_FEATURES_AND_SCOPE.md & 04_ARCHITECTURE.md
        var fallbackState = state switch
        {
            CharacterState.Walk => Animations.ContainsKey(CharacterState.Walk) ? CharacterState.Walk : CharacterState.Idle,
            CharacterState.Run => Animations.ContainsKey(CharacterState.Walk) ? CharacterState.Walk : CharacterState.Idle,
            CharacterState.Jump => Animations.ContainsKey(CharacterState.Happy) ? CharacterState.Happy : CharacterState.Idle,
            CharacterState.Talking => Animations.ContainsKey(CharacterState.Listening) ? CharacterState.Listening : CharacterState.Idle,
            CharacterState.Error => Animations.ContainsKey(CharacterState.Thinking) ? CharacterState.Thinking : CharacterState.Idle,
            CharacterState.WaitingForApproval => Animations.ContainsKey(CharacterState.Thinking) ? CharacterState.Thinking : (Animations.ContainsKey(CharacterState.Listening) ? CharacterState.Listening : CharacterState.Idle),
            CharacterState.TaskComplete => Animations.ContainsKey(CharacterState.Happy) ? CharacterState.Happy : CharacterState.Idle,
            CharacterState.Busy => Animations.ContainsKey(CharacterState.Working) ? CharacterState.Working : CharacterState.Idle,
            _ => CharacterState.Idle
        };

        if (Animations.TryGetValue(fallbackState, out var fallbackAnim))
        {
            return fallbackAnim;
        }

        return Animations[CharacterState.Idle];
    }
}
