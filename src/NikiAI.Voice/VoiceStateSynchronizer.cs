using Microsoft.Extensions.Logging;
using NikiAI.Core.Character;
using NikiAI.Core.Voice;

namespace NikiAI.Voice;

/// <summary>
/// Synchronizes voice interaction states with the existing Character Runtime state machine.
/// Does not create a second voice-driven character state machine; translates voice states
/// into approved CharacterState transitions.
/// </summary>
public class VoiceStateSynchronizer : IDisposable
{
    private readonly IVoiceService _voiceService;
    private readonly ICharacterStateMachine _stateMachine;
    private readonly ILogger<VoiceStateSynchronizer>? _logger;
    private bool _isDisposed;

    public VoiceStateSynchronizer(
        IVoiceService voiceService,
        ICharacterStateMachine stateMachine,
        ILogger<VoiceStateSynchronizer>? logger = null)
    {
        _voiceService = voiceService ?? throw new ArgumentNullException(nameof(voiceService));
        _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
        _logger = logger;

        _voiceService.StateChanged += OnVoiceStateChanged;
    }

    private void OnVoiceStateChanged(object? sender, VoiceSessionState voiceState)
    {
        if (_isDisposed) return;

        try
        {
            switch (voiceState)
            {
                case VoiceSessionState.Listening:
                    _stateMachine.SetState(CharacterState.Listening);
                    break;

                case VoiceSessionState.Thinking:
                    _stateMachine.SetState(CharacterState.Thinking);
                    break;

                case VoiceSessionState.Speaking:
                    _stateMachine.SetState(CharacterState.Talking);
                    break;

                case VoiceSessionState.Waiting:
                    _stateMachine.SetState(CharacterState.WaitingForApproval);
                    break;

                case VoiceSessionState.Error:
                    _stateMachine.TriggerReaction(CharacterState.Error, TimeSpan.FromMilliseconds(1500), CharacterState.Idle);
                    break;

                case VoiceSessionState.Completed:
                    _stateMachine.TriggerReaction(CharacterState.TaskComplete, TimeSpan.FromMilliseconds(1500), CharacterState.Idle);
                    break;

                case VoiceSessionState.Idle:
                default:
                    if (_stateMachine.CurrentState is CharacterState.Listening or CharacterState.Thinking or CharacterState.Talking)
                    {
                        _stateMachine.SetState(CharacterState.Idle);
                    }
                    break;
            }

            _logger?.LogDebug("Voice state {VoiceState} mapped to character state machine.", voiceState);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error synchronizing voice state {VoiceState} with character state machine.", voiceState);
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _voiceService.StateChanged -= OnVoiceStateChanged;
    }
}
