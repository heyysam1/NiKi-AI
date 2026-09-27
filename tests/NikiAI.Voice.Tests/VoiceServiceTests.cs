using NikiAI.Core.Voice;
using NikiAI.Voice;

namespace NikiAI.Voice.Tests;

public class VoiceServiceTests : IDisposable
{
    private readonly TextFallbackSpeechToTextService _sttService;
    private readonly TextFallbackTextToSpeechService _ttsService;
    private readonly VoiceService _voiceService;

    public VoiceServiceTests()
    {
        _sttService = new TextFallbackSpeechToTextService();
        _ttsService = new TextFallbackTextToSpeechService();
        _voiceService = new VoiceService(_sttService, _ttsService);
    }

    public void Dispose()
    {
        _voiceService.Dispose();
    }

    [Fact]
    public async Task PushToTalk_TransitionsStates_AndCompletesWithResult()
    {
        var states = new List<VoiceSessionState>();
        _voiceService.StateChanged += (s, state) => states.Add(state);

        _sttService.SetInputText("Hello Niki, what is the plan today?");

        // 1. Start Push-to-Talk
        await _voiceService.StartPushToTalkAsync();
        Assert.Equal(VoiceSessionState.Listening, _voiceService.State);

        // 2. Stop Push-to-Talk and verify outcome
        var result = await _voiceService.StopPushToTalkAsync();

        Assert.True(result.Success);
        Assert.Equal("Hello Niki, what is the plan today?", result.Prompt);
        Assert.False(string.IsNullOrWhiteSpace(result.ResponseText));
        Assert.Equal(VoiceSessionState.Idle, _voiceService.State);

        // Verify TTS was invoked
        Assert.NotEmpty(_ttsService.SpokenUtterances);
        Assert.Contains(result.ResponseText, _ttsService.SpokenUtterances);

        // Verify state sequence included Listening, Thinking, Speaking, Completed, Idle
        Assert.Contains(VoiceSessionState.Listening, states);
        Assert.Contains(VoiceSessionState.Thinking, states);
        Assert.Contains(VoiceSessionState.Speaking, states);
        Assert.Contains(VoiceSessionState.Completed, states);
        Assert.Contains(VoiceSessionState.Idle, states);
    }

    [Fact]
    public async Task ProcessTextCommandAsync_ExecutesWithoutMicrophoneHardware()
    {
        var result = await _voiceService.ProcessTextCommandAsync("Check system status");

        Assert.True(result.Success);
        Assert.Equal("Check system status", result.Prompt);
        Assert.NotEmpty(_ttsService.SpokenUtterances);
        Assert.Equal(VoiceSessionState.Idle, _voiceService.State);
    }

    [Fact]
    public async Task ToggleListening_StartsAndStopsListening()
    {
        _sttService.SetInputText("Toggle test command");

        // First toggle: starts listening
        var firstResult = await _voiceService.ToggleListeningAsync();
        Assert.Null(firstResult);
        Assert.Equal(VoiceSessionState.Listening, _voiceService.State);

        // Second toggle: stops listening and returns result
        var secondResult = await _voiceService.ToggleListeningAsync();
        Assert.NotNull(secondResult);
        Assert.True(secondResult.Success);
        Assert.Equal("Toggle test command", secondResult.Prompt);
        Assert.Equal(VoiceSessionState.Idle, _voiceService.State);
    }

    [Fact]
    public async Task PushToTalkController_ManagesPressAndRelease()
    {
        var controller = new PushToTalkController(_voiceService);
        _sttService.SetInputText("Controller test command");

        Assert.False(controller.IsPressed);

        // Key down
        await controller.OnKeyDownAsync();
        Assert.True(controller.IsPressed);
        Assert.Equal(VoiceSessionState.Listening, _voiceService.State);

        // Key repeat ignored
        await controller.OnKeyDownAsync();
        Assert.Equal(VoiceSessionState.Listening, _voiceService.State);

        // Key up
        var result = await controller.OnKeyUpAsync();
        Assert.False(controller.IsPressed);
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal("Controller test command", result.Prompt);
    }

    [Fact]
    public async Task WhenAudioOutputDisabled_TtsIsNotInvoked()
    {
        _voiceService.IsAudioOutputEnabled = false;

        var result = await _voiceService.ProcessTextCommandAsync("Silent command");

        Assert.True(result.Success);
        Assert.Empty(_ttsService.SpokenUtterances);
    }

    [Fact]
    public async Task CancelCurrentSession_ResetsStateToIdle()
    {
        await _voiceService.StartPushToTalkAsync();
        Assert.Equal(VoiceSessionState.Listening, _voiceService.State);

        _voiceService.CancelCurrentSession();
        Assert.Equal(VoiceSessionState.Idle, _voiceService.State);
    }
}
