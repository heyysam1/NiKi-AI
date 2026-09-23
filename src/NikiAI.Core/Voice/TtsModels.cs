namespace NikiAI.Core.Voice;

/// <summary>
/// Metadata describing an available synthesized voice.
/// </summary>
public record VoiceDescriptor(
    string Id,
    string Name,
    string? Gender = null,
    string? Locale = null,
    string? ProviderId = null
);

/// <summary>
/// Non-secret configuration for a Text-to-Speech provider.
/// API keys/secrets are strictly excluded and managed via ISecureSettingsStore by ProviderId.
/// </summary>
public class TtsProviderConfig
{
    public string ProviderId { get; set; } = "windows-sapi";
    public string EndpointUrl { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public string VoiceId { get; set; } = string.Empty;
    public double Speed { get; set; } = 1.0;
}

/// <summary>
/// Result from a speech synthesis provider.
/// </summary>
public record TtsSynthesisResult(
    bool Success,
    Stream? AudioStream,
    TimeSpan Duration,
    string? ErrorMessage = null
)
{
    public static TtsSynthesisResult Succeeded(Stream stream, TimeSpan duration) =>
        new(true, stream, duration, null);

    public static TtsSynthesisResult Failed(string error) =>
        new(false, null, TimeSpan.Zero, error);
}
