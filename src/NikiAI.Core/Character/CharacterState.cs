namespace NikiAI.Core.Character;

/// <summary>
/// Character states as specified in 02_FEATURES_AND_SCOPE.md and 04_ARCHITECTURE.md.
/// </summary>
public enum CharacterState
{
    Idle,
    Walk,
    Run,
    Jump,
    Listening,
    Thinking,
    Working,
    Talking,
    Happy,
    Notification,
    Sleep,
    Error,
    WaitingForApproval,
    TaskComplete,
    Busy
}

/// <summary>
/// Decoupled character state machine contract.
/// </summary>
public interface ICharacterStateMachine
{
    CharacterState CurrentState { get; }
    void SetState(CharacterState newState);
    void TriggerReaction(CharacterState reactionState, TimeSpan duration, CharacterState? returnState = null);
    event EventHandler<CharacterState>? StateChanged;
}
