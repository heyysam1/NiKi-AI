using System.Speech.Synthesis;
using Microsoft.Extensions.Logging;
using NikiAI.Core.Voice;

namespace NikiAI.Voice;

/// <summary>
/// Windows SAPI text-to-speech synthesis service.
/// Provides spoken audio responses with configurable rate, volume, and clean cancellation.
/// </summary>
public class WindowsTextToSpeechService : ITextToSpeechService, IDisposable
{
    private readonly ILogger<WindowsTextToSpeechService>? _logger;
    private SpeechSynthesizer? _synthesizer;
    private bool _isDisposed;
    private readonly object _lock = new();

    public bool IsAvailable { get; private set; }
    public bool IsSpeaking => _synthesizer?.State == SynthesizerState.Speaking;

    public WindowsTextToSpeechService(ILogger<WindowsTextToSpeechService>? logger = null)
    {
        _logger = logger;
        InitializeSynthesizer();
    }

    private void InitializeSynthesizer()
    {
        try
        {
            _synthesizer = new SpeechSynthesizer();
            _synthesizer.SetOutputToDefaultAudioDevice();
            _synthesizer.Rate = 0; // Normal rate (-10 to 10)
            _synthesizer.Volume = 90; // 0 to 100

            var installedVoices = _synthesizer.GetInstalledVoices();
            if (installedVoices.Count > 0)
            {
                // Prefer a natural female voice (like Microsoft Zira) for Niki if available
                var preferredVoice = installedVoices.FirstOrDefault(v => v.Enabled && v.VoiceInfo.Gender == VoiceGender.Female);
                if (preferredVoice != null)
                {
                    _synthesizer.SelectVoice(preferredVoice.VoiceInfo.Name);
                }
            }

            IsAvailable = true;
            _logger?.LogInformation("Windows Text-to-Speech synthesizer initialized.");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to initialize Windows Text-to-Speech synthesizer. Audio output disabled.");
            IsAvailable = false;
            _synthesizer?.Dispose();
            _synthesizer = null;
        }
    }

    public Task SpeakAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text) || !IsAvailable || _synthesizer == null)
        {
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource<bool>();

        lock (_lock)
        {
            try
            {
                // Stop any previous speech
                _synthesizer.SpeakAsyncCancelAll();

                void OnSpeakCompleted(object? sender, SpeakCompletedEventArgs e)
                {
                    if (_synthesizer != null)
                    {
                        _synthesizer.SpeakCompleted -= OnSpeakCompleted;
                    }
                    if (e.Cancelled)
                    {
                        tcs.TrySetCanceled(cancellationToken);
                    }
                    else if (e.Error != null)
                    {
                        tcs.TrySetException(e.Error);
                    }
                    else
                    {
                        tcs.TrySetResult(true);
                    }
                }

                _synthesizer.SpeakCompleted += OnSpeakCompleted;

                if (cancellationToken.CanBeCanceled)
                {
                    cancellationToken.Register(() =>
                    {
                        StopSpeaking();
                        tcs.TrySetCanceled(cancellationToken);
                    });
                }

                _synthesizer.SpeakAsync(text);
                _logger?.LogDebug("Started speaking text (length: {Length})", text.Length);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to speak text.");
                tcs.TrySetException(ex);
            }
        }

        return tcs.Task;
    }

    public void StopSpeaking()
    {
        lock (_lock)
        {
            try
            {
                _synthesizer?.SpeakAsyncCancelAll();
            }
            catch { }
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        if (_synthesizer != null)
        {
            try
            {
                _synthesizer.SpeakAsyncCancelAll();
                _synthesizer.Dispose();
            }
            catch { }
            _synthesizer = null;
        }
    }
}
