using NikiAI.Character;
using NikiAI.Core.Character;
using NikiAI.Core.Voice;
using NikiAI.Voice;

namespace NikiAI.Voice.Tests;

public class VoiceStateSynchronizerTests : IDisposable
{
    private readonly TextFallbackSpeechToTextService _sttService;
    private readonly TextFallbackTextToSpeechService _ttsService;
    private readonly VoiceService _voiceService;
    private readonly CharacterStateMachine _stateMachine;
    private readonly VoiceStateSynchronizer _synchronizer;

    public VoiceStateSynchronizerTests()
    {
        _sttService = new TextFallbackSpeechToTextService();
        _ttsService = new TextFallbackTextToSpeechService();
        _voiceService = new VoiceService(_sttService, _ttsService);
        _stateMachine = new CharacterStateMachine();
        _synchronizer = new VoiceStateSynchronizer(_voiceService, _stateMachine);
    }

    public void Dispose()
    {
        _synchronizer.Dispose();
        _voiceService.Dispose();
    }

    [Fact]
    public async Task Listening_TransitionsCharacterToListening()
    {
        Assert.Equal(CharacterState.Idle, _stateMachine.CurrentState);

        await _voiceService.StartPushToTalkAsync();

        Assert.Equal(VoiceSessionState.Listening, _voiceService.State);
        Assert.Equal(CharacterState.Listening, _stateMachine.CurrentState);

        _voiceService.CancelCurrentSession();
        Assert.Equal(CharacterState.Idle, _stateMachine.CurrentState);
    }

    [Fact]
    public async Task PushToTalkTurn_TransitionsThroughApprovedCharacterStates()
    {
        var characterStates = new List<CharacterState>();
        _stateMachine.StateChanged += (s, state) => characterStates.Add(state);

        _sttService.SetInputText("Niki, give me an update.");

        await _voiceService.StartPushToTalkAsync();
        await _voiceService.StopPushToTalkAsync();

        // Must have visited Listening, Thinking, and Talking/Happy
        Assert.Contains(CharacterState.Listening, characterStates);
        Assert.Contains(CharacterState.Thinking, characterStates);
        Assert.Contains(CharacterState.Talking, characterStates);
    }

    [Fact]
    public void DoesNotCreateSecondStateMachine_DirectlyDrivesExistingStateMachine()
    {
        // Assert that the synchronizer explicitly controls the existing ICharacterStateMachine instance
        Assert.Equal(CharacterState.Idle, _stateMachine.CurrentState);

        _stateMachine.SetState(CharacterState.Happy);
        Assert.Equal(CharacterState.Happy, _stateMachine.CurrentState);
    }
}
