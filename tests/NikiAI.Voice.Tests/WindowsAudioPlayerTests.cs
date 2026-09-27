using System.Text;
using NikiAI.Voice;
using Xunit;

namespace NikiAI.Voice.Tests;

public class WindowsAudioPlayerTests
{
    [Fact]
    public void CreateSilentWav_GeneratesValidRiffWaveHeader()
    {
        var wav = WindowsAudioPlayer.CreateSilentWav(durationMs: 50, sampleRate: 8000);

        Assert.NotNull(wav);
        Assert.True(wav.Length >= 44);

        // Verify "RIFF"
        var riffHeader = Encoding.ASCII.GetString(wav, 0, 4);
        Assert.Equal("RIFF", riffHeader);

        // Verify "WAVE"
        var waveFormat = Encoding.ASCII.GetString(wav, 8, 4);
        Assert.Equal("WAVE", waveFormat);

        // Verify "fmt "
        var fmtChunk = Encoding.ASCII.GetString(wav, 12, 4);
        Assert.Equal("fmt ", fmtChunk);

        // Verify "data"
        var dataChunk = Encoding.ASCII.GetString(wav, 36, 4);
        Assert.Equal("data", dataChunk);
    }

    [Fact]
    public async Task PlayAsync_WithEmptyBytes_ReturnsImmediately()
    {
        using var player = new WindowsAudioPlayer();

        await player.PlayAsync(Array.Empty<byte>());
        Assert.False(player.IsPlaying);

        await player.PlayAsync(null!);
        Assert.False(player.IsPlaying);
    }

    [Fact]
    public async Task PlayAsync_WithValidWav_ExecutesCleanly()
    {
        using var player = new WindowsAudioPlayer();
        var wav = WindowsAudioPlayer.CreateSilentWav(durationMs: 30);

        await player.PlayAsync(wav);
        Assert.False(player.IsPlaying);
    }

    [Fact]
    public async Task PlayAsync_Cancellation_InterruptsPlayback()
    {
        using var player = new WindowsAudioPlayer();
        var wav = WindowsAudioPlayer.CreateSilentWav(durationMs: 1000);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(40));

        try
        {
            await player.PlayAsync(wav, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        Assert.False(player.IsPlaying);
    }

    [Fact]
    public async Task Stop_StopsActivePlaybackImmediately()
    {
        using var player = new WindowsAudioPlayer();
        var wav = WindowsAudioPlayer.CreateSilentWav(durationMs: 1000);

        var playTask = player.PlayAsync(wav);
        await Task.Delay(20);

        player.Stop();
        Assert.False(player.IsPlaying);

        try
        {
            await playTask;
        }
        catch (OperationCanceledException) { }

        Assert.False(player.IsPlaying);
    }

    [Fact]
    public async Task Replacement_SecondPlayAsync_CancelsFirstPlayback()
    {
        using var player = new WindowsAudioPlayer();
        var longWav = WindowsAudioPlayer.CreateSilentWav(durationMs: 1000);
        var shortWav = WindowsAudioPlayer.CreateSilentWav(durationMs: 30);

        var firstTask = player.PlayAsync(longWav);
        await Task.Delay(20);

        // Second play call triggers replacement of the first
        var secondTask = player.PlayAsync(shortWav);

        try
        {
            await firstTask;
        }
        catch (OperationCanceledException) { }

        await secondTask;
        Assert.False(player.IsPlaying);
    }

    [Fact]
    public async Task ConcurrencySerialization_SerializesMultiplePlayCalls()
    {
        using var player = new WindowsAudioPlayer();
        var wav1 = WindowsAudioPlayer.CreateSilentWav(durationMs: 40);
        var wav2 = WindowsAudioPlayer.CreateSilentWav(durationMs: 40);

        var t1 = player.PlayAsync(wav1);
        var t2 = player.PlayAsync(wav2);

        await Task.WhenAll(
            Task.Run(async () => { try { await t1; } catch (OperationCanceledException) { } }),
            Task.Run(async () => { try { await t2; } catch (OperationCanceledException) { } })
        );

        Assert.False(player.IsPlaying);
    }

    [Fact]
    public async Task Dispose_StopsPlayback_AndThrowsOnSubsequentCalls()
    {
        var player = new WindowsAudioPlayer();
        var wav = WindowsAudioPlayer.CreateSilentWav(durationMs: 50);

        player.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => player.PlayAsync(wav));
    }
}
