namespace NikiAI.Core.Voice;

/// <summary>
/// Universal provider contract for Text-to-Speech audio synthesis.
/// Decoupled from specific AI speech vendors (Windows SAPI, OpenAI, ElevenLabs, custom HTTP).
/// Credentials are resolved in implementations using ProviderId.
/// </summary>
public interface ITtsProvider
{
    /// <summary>
    /// Unique provider identifier (e.g. "windows-sapi", "openai-tts", "custom-tts").
    /// </summary>
    string ProviderId { get; }

    /// <summary>
    /// User-friendly display name.
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Indicates whether the provider is currently available and ready for synthesis.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Enumerates voices available from this provider.
    /// </summary>
    Task<IReadOnlyList<VoiceDescriptor>> GetVoicesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Synthesizes spoken audio from text and returns an audio stream (e.g. WAV format).
    /// </summary>
    Task<Stream> SynthesizeSpeechAsync(string text, string? voiceId = null, CancellationToken cancellationToken = default);
}
