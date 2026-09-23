using System.IO;
using Microsoft.Extensions.Logging;
using NikiAI.Core.Voice;

namespace NikiAI.Voice;

/// <summary>
/// Pluggable, resilient Text-to-Speech service implementing ITextToSpeechService.
/// Orchestrates generic ITtsProvider implementations (OpenAI, custom HTTP, etc.) with native
/// WindowsAudioPlayer playback and automatic resilient fallback to local Windows SAPI.
/// Allows seamless injection into existing VoiceService without code changes.
/// </summary>
public class PluggableTextToSpeechService : ITextToSpeechService, IDisposable
{
    private readonly IEnumerable<ITtsProvider> _providers;
    private readonly WindowsAudioPlayer _audioPlayer;
    private readonly ITextToSpeechService _fallbackService;
    private readonly ILogger<PluggableTextToSpeechService>? _logger;
    private bool _isDisposed;

    public string ActiveProviderId { get; set; } = "openai-tts";
    public string? ActiveVoiceId { get; set; }

    public bool IsAvailable =>
        _providers.Any(p => p.IsAvailable) || _fallbackService.IsAvailable;

    public bool IsSpeaking =>
        _audioPlayer.IsPlaying || _fallbackService.IsSpeaking;

    public IReadOnlyList<ITtsProvider> Providers => _providers.ToList();

    public PluggableTextToSpeechService(
        IEnumerable<ITtsProvider> providers,
        WindowsAudioPlayer audioPlayer,
        ITextToSpeechService fallbackService,
        ILogger<PluggableTextToSpeechService>? logger = null)
    {
        _providers = providers ?? throw new ArgumentNullException(nameof(providers));
        _audioPlayer = audioPlayer ?? throw new ArgumentNullException(nameof(audioPlayer));
        _fallbackService = fallbackService ?? throw new ArgumentNullException(nameof(fallbackService));
        _logger = logger;
    }

    public async Task SpeakAsync(string text, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        // 1. Try active AI provider if not specifically configured for SAPI
        var provider = _providers.FirstOrDefault(p =>
            p.ProviderId.Equals(ActiveProviderId, StringComparison.OrdinalIgnoreCase));

        if (provider != null && !provider.ProviderId.Equals("windows-sapi", StringComparison.OrdinalIgnoreCase) && provider.IsAvailable)
        {
            try
            {
                _logger?.LogDebug("Synthesizing speech via provider '{ProviderId}'...", provider.ProviderId);

                await using var stream = await provider.SynthesizeSpeechAsync(text, ActiveVoiceId, cancellationToken);
                byte[] audioBytes;
                if (stream is MemoryStream ms)
                {
                    audioBytes = ms.ToArray();
                }
                else
                {
                    using var copyMs = new MemoryStream();
                    await stream.CopyToAsync(copyMs, cancellationToken);
                    audioBytes = copyMs.ToArray();
                }

                if (audioBytes.Length > 0)
                {
                    await _audioPlayer.PlayAsync(audioBytes, cancellationToken);
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                _logger?.LogDebug("Speech synthesis or playback cancelled.");
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Active TTS provider '{ProviderId}' failed during synthesis/playback. Falling back to Windows SAPI.", provider.ProviderId);
            }
        }

        // 2. Resilient fallback to Windows SAPI
        _logger?.LogInformation("Using resilient Windows SAPI fallback for speech output.");
        await _fallbackService.SpeakAsync(text, cancellationToken);
    }

    public void StopSpeaking()
    {
        try
        {
            _audioPlayer.Stop();
        }
        catch { }

        try
        {
            _fallbackService.StopSpeaking();
        }
        catch { }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        StopSpeaking();
        _audioPlayer.Dispose();
        if (_fallbackService is IDisposable disposableFallback)
        {
            disposableFallback.Dispose();
        }
    }
}
