using System.IO;
using System.Net.Http;
using NikiAI.Core.Voice;
using NikiAI.Voice;
using Xunit;

namespace NikiAI.Voice.Tests;

public class PluggableTtsServiceTests
{
    private class MockTtsProvider : ITtsProvider
    {
        public string ProviderId { get; set; } = "mock-ai-tts";
        public string DisplayName => "Mock AI TTS";
        public bool IsAvailable { get; set; } = true;
        public int SynthesizeCount { get; private set; }
        public bool ShouldThrow { get; set; }

        public Task<IReadOnlyList<VoiceDescriptor>> GetVoicesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<VoiceDescriptor>>(new List<VoiceDescriptor>
            {
                new("voice-1", "Voice One", "Female", "en-US", ProviderId)
            });
        }

        public Task<Stream> SynthesizeSpeechAsync(string text, string? voiceId = null, CancellationToken cancellationToken = default)
        {
            SynthesizeCount++;
            if (ShouldThrow)
            {
                throw new HttpRequestException("Mock network outage 503 Service Unavailable");
            }

            var wav = WindowsAudioPlayer.CreateSilentWav(durationMs: 20);
            return Task.FromResult<Stream>(new MemoryStream(wav));
        }
    }

    private class MockFallbackTtsService : ITextToSpeechService
    {
        public bool IsAvailable => true;
        public bool IsSpeaking { get; private set; }
        public int SpeakCount { get; private set; }
        public bool StopCalled { get; private set; }

        public Task SpeakAsync(string text, CancellationToken cancellationToken = default)
        {
            SpeakCount++;
            return Task.CompletedTask;
        }

        public void StopSpeaking()
        {
            StopCalled = true;
            IsSpeaking = false;
        }
    }

    [Fact]
    public async Task SpeakAsync_WithActiveAiProvider_SynthesizesAndPlaysAudio()
    {
        var mockProvider = new MockTtsProvider { ProviderId = "openai-tts" };
        var player = new WindowsAudioPlayer();
        var fallback = new MockFallbackTtsService();

        using var service = new PluggableTextToSpeechService(
            new[] { mockProvider },
            player,
            fallback)
        {
            ActiveProviderId = "openai-tts"
        };

        await service.SpeakAsync("Hello, Niki!");

        Assert.Equal(1, mockProvider.SynthesizeCount);
        Assert.Equal(0, fallback.SpeakCount); // Did not need fallback
    }

    [Fact]
    public async Task SpeakAsync_WhenAiProviderFails_ResilientlyFallsBackToSapi()
    {
        var failingProvider = new MockTtsProvider
        {
            ProviderId = "openai-tts",
            ShouldThrow = true
        };
        var player = new WindowsAudioPlayer();
        var fallback = new MockFallbackTtsService();

        using var service = new PluggableTextToSpeechService(
            new[] { failingProvider },
            player,
            fallback)
        {
            ActiveProviderId = "openai-tts"
        };

        // Must NOT throw exception to caller
        await service.SpeakAsync("Hello resilient world!");

        Assert.Equal(1, failingProvider.SynthesizeCount);
        Assert.Equal(1, fallback.SpeakCount); // Resilient fallback invoked!
    }

    [Fact]
    public async Task SpeakAsync_WhenActiveProviderIsSapi_RoutesDirectlyToFallback()
    {
        var mockProvider = new MockTtsProvider { ProviderId = "openai-tts" };
        var player = new WindowsAudioPlayer();
        var fallback = new MockFallbackTtsService();

        using var service = new PluggableTextToSpeechService(
            new[] { mockProvider },
            player,
            fallback)
        {
            ActiveProviderId = "windows-sapi"
        };

        await service.SpeakAsync("Hello Windows SAPI!");

        Assert.Equal(0, mockProvider.SynthesizeCount); // Skipped AI provider
        Assert.Equal(1, fallback.SpeakCount);
    }

    [Fact]
    public void StopSpeaking_StopsBothPlayerAndFallbackService()
    {
        var mockProvider = new MockTtsProvider();
        var player = new WindowsAudioPlayer();
        var fallback = new MockFallbackTtsService();

        using var service = new PluggableTextToSpeechService(
            new[] { mockProvider },
            player,
            fallback);

        service.StopSpeaking();

        Assert.True(fallback.StopCalled);
        Assert.False(player.IsPlaying);
    }

    [Fact]
    public async Task SpeakAsync_WithEmptyString_DoesNothing()
    {
        var mockProvider = new MockTtsProvider();
        var player = new WindowsAudioPlayer();
        var fallback = new MockFallbackTtsService();

        using var service = new PluggableTextToSpeechService(
            new[] { mockProvider },
            player,
            fallback);

        await service.SpeakAsync("   ");

        Assert.Equal(0, mockProvider.SynthesizeCount);
        Assert.Equal(0, fallback.SpeakCount);
    }
}
