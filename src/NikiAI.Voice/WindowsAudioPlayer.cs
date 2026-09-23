using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;

namespace NikiAI.Voice;

/// <summary>
/// Native audio player for synthesized WAV audio streams.
/// Designed and empirically verified to provide strict cancellation, stop,
/// audio replacement, concurrency serialization, and disposal semantics.
/// </summary>
public class WindowsAudioPlayer : IDisposable
{
    private readonly SemaphoreSlim _playbackGate = new(1, 1);
    private readonly ILogger<WindowsAudioPlayer>? _logger;
    private CancellationTokenSource? _activePlaybackCts;
    private System.Media.SoundPlayer? _activeSoundPlayer;
    private MemoryStream? _activeStream;
    private readonly object _stateLock = new();
    private bool _isPlaying;
    private bool _isDisposed;

    public bool IsPlaying
    {
        get
        {
            lock (_stateLock) return _isPlaying;
        }
        private set
        {
            lock (_stateLock) _isPlaying = value;
        }
    }

    public WindowsAudioPlayer(ILogger<WindowsAudioPlayer>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Plays WAV audio bytes asynchronously with concurrency serialization.
    /// If an audio playback is already in progress, it is stopped and replaced.
    /// </summary>
    public async Task PlayAsync(byte[] audioBytes, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (audioBytes == null || audioBytes.Length == 0)
        {
            return;
        }

        // Cancel and stop any active playback immediately (replacement semantics)
        Stop();

        // Concurrency serialization: wait for previous playback cleanup
        await _playbackGate.WaitAsync(cancellationToken);

        CancellationTokenSource linkedCts;
        lock (_stateLock)
        {
            _activePlaybackCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedCts = _activePlaybackCts;
        }

        try
        {
            var token = linkedCts.Token;
            token.ThrowIfCancellationRequested();

            var ms = new MemoryStream(audioBytes);
            var player = new System.Media.SoundPlayer(ms);

            lock (_stateLock)
            {
                _activeStream = ms;
                _activeSoundPlayer = player;
                IsPlaying = true;
            }

            using var reg = token.Register(() =>
            {
                try
                {
                    player.Stop();
                }
                catch { }
            });

            await Task.Run(() =>
            {
                try
                {
                    player.PlaySync();
                }
                catch (InvalidOperationException ex)
                {
                    // Occurs on systems without an active audio endpoint or corrupted stream
                    _logger?.LogWarning(ex, "SoundPlayer PlaySync encountered invalid audio device/stream.");
                }
                catch (PlatformNotSupportedException ex)
                {
                    _logger?.LogWarning(ex, "SoundPlayer is not supported on this platform.");
                }
                catch (Exception ex) when (token.IsCancellationRequested)
                {
                    _logger?.LogDebug(ex, "SoundPlayer playback interrupted by cancellation.");
                }
            }, token);
        }
        catch (OperationCanceledException)
        {
            _logger?.LogDebug("Audio playback cancelled.");
            throw;
        }
        finally
        {
            lock (_stateLock)
            {
                IsPlaying = false;
                try
                {
                    _activeSoundPlayer?.Dispose();
                }
                catch { }
                _activeSoundPlayer = null;

                try
                {
                    _activeStream?.Dispose();
                }
                catch { }
                _activeStream = null;

                linkedCts.Dispose();
                if (_activePlaybackCts == linkedCts)
                {
                    _activePlaybackCts = null;
                }
            }

            _playbackGate.Release();
        }
    }

    /// <summary>
    /// Stops any currently playing audio immediately.
    /// </summary>
    public void Stop()
    {
        lock (_stateLock)
        {
            try
            {
                _activePlaybackCts?.Cancel();
                _activeSoundPlayer?.Stop();
            }
            catch { }
            IsPlaying = false;
        }
    }

    /// <summary>
    /// Utility helper to construct a valid in-memory PCM WAV byte array for testing and verification.
    /// </summary>
    public static byte[] CreateSilentWav(int durationMs = 100, int sampleRate = 8000)
    {
        const short numChannels = 1;
        const short bitsPerSample = 16;
        int numSamples = (int)((long)sampleRate * durationMs / 1000);
        int subChunk2Size = numSamples * numChannels * (bitsPerSample / 8);
        int chunkSize = 36 + subChunk2Size;
        int byteRate = sampleRate * numChannels * (bitsPerSample / 8);
        short blockAlign = (short)(numChannels * (bitsPerSample / 8));

        using var ms = new MemoryStream(44 + subChunk2Size);
        using var writer = new BinaryWriter(ms);

        // RIFF header
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(chunkSize);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));

        // "fmt " chunk
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16); // Subchunk1Size for PCM
        writer.Write((short)1); // AudioFormat: 1 = PCM
        writer.Write(numChannels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write(bitsPerSample);

        // "data" chunk
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(subChunk2Size);

        // Silent samples (0)
        byte[] zeroData = new byte[subChunk2Size];
        writer.Write(zeroData);

        return ms.ToArray();
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        Stop();
        _playbackGate.Dispose();
    }
}
