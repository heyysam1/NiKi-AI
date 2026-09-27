using NikiAI.Core.Memory;
using NikiAI.Core.Voice;
using NikiAI.Memory;
using NikiAI.Voice;

namespace NikiAI.Voice.Tests;

public class VoiceNegativeSecurityTests : IDisposable
{
    private readonly TextFallbackSpeechToTextService _sttService;
    private readonly TextFallbackTextToSpeechService _ttsService;
    private readonly VoiceService _voiceService;
    private readonly InMemoryMemoryStore _memoryStore;
    private readonly InMemoryTimelineRepository _timelineRepository;

    public VoiceNegativeSecurityTests()
    {
        _sttService = new TextFallbackSpeechToTextService();
        _ttsService = new TextFallbackTextToSpeechService();
        _voiceService = new VoiceService(_sttService, _ttsService);
        _memoryStore = new InMemoryMemoryStore();
        _timelineRepository = new InMemoryTimelineRepository();
    }

    public void Dispose()
    {
        _voiceService.Dispose();
    }

    [Fact]
    public async Task NegativeTest_VoiceCommand_DoesNotSilentlyWriteToLongTermMemory()
    {
        var initialMemories = await _memoryStore.GetMemoriesAsync(MemoryCategory.LongTerm);
        Assert.Empty(initialMemories);

        _sttService.SetInputText("Remember that my secret code is 987654");
        await _voiceService.StartPushToTalkAsync();
        var result = await _voiceService.StopPushToTalkAsync();

        Assert.True(result.Success);

        // Voice turns must NEVER silently write to permanent memory without explicit user approval / memory tool
        var postMemories = await _memoryStore.GetMemoriesAsync(MemoryCategory.LongTerm);
        Assert.Empty(postMemories);
    }

    [Fact]
    public async Task NegativeTest_VoiceAudio_DoesNotSpamTimelineEvents()
    {
        var initialTimeline = await _timelineRepository.GetRecentEventsAsync(50);
        Assert.Empty(initialTimeline);

        _sttService.SetInputText("Just chatting casually with voice");
        await _voiceService.StartPushToTalkAsync();
        await _voiceService.StopPushToTalkAsync();

        var postTimeline = await _timelineRepository.GetRecentEventsAsync(50);
        Assert.Empty(postTimeline);
    }

    [Fact]
    public async Task NegativeTest_EmptyOrWhitespaceCommand_RejectsExecution()
    {
        var result = await _voiceService.ProcessTextCommandAsync("   ");
        Assert.False(result.Success);
        Assert.Contains("cannot be empty", result.ErrorMessage);
    }
}
