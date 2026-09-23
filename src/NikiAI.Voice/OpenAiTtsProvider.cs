using System.Net.Http;
using Microsoft.Extensions.Logging;
using NikiAI.Core.Security;
using NikiAI.Core.Voice;

namespace NikiAI.Voice;

/// <summary>
/// OpenAI implementation of ITtsProvider targeting natural speech synthesis.
/// Resolves API credentials using provider-scoped key 'tts_credential_openai-tts',
/// with graceful fallback to 'openai_api_key' if previously configured.
/// </summary>
public class OpenAiTtsProvider : CustomHttpTtsProvider
{
    public const string DefaultProviderId = "openai-tts";
    private readonly ISecureSettingsStore _secureStore;

    private static readonly IReadOnlyList<VoiceDescriptor> KnownVoices = new List<VoiceDescriptor>
    {
        new("nova", "Nova (Warm, natural female)", "Female", "en-US", DefaultProviderId),
        new("shimmer", "Shimmer (Expressive, clear female)", "Female", "en-US", DefaultProviderId),
        new("alloy", "Alloy (Neutral, versatile)", "Neutral", "en-US", DefaultProviderId),
        new("echo", "Echo (Balanced male)", "Male", "en-US", DefaultProviderId),
        new("fable", "Fable (British accent)", "Neutral", "en-GB", DefaultProviderId),
        new("onyx", "Onyx (Deep, authoritative male)", "Male", "en-US", DefaultProviderId)
    };

    public OpenAiTtsProvider(
        HttpClient httpClient,
        ISecureSettingsStore secureSettingsStore,
        string endpointUrl = "https://api.openai.com/v1/audio/speech",
        string defaultModel = "tts-1",
        string defaultVoiceId = "nova",
        ILogger<OpenAiTtsProvider>? logger = null)
        : base(
            httpClient,
            secureSettingsStore,
            providerId: DefaultProviderId,
            displayName: "OpenAI Natural Voice",
            endpointUrl: endpointUrl,
            defaultModel: defaultModel,
            defaultVoiceId: defaultVoiceId)
    {
        _secureStore = secureSettingsStore ?? throw new ArgumentNullException(nameof(secureSettingsStore));
    }

    public override Task<IReadOnlyList<VoiceDescriptor>> GetVoicesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(KnownVoices);
    }

    protected override async Task<string?> ResolveCredentialAsync(CancellationToken cancellationToken)
    {
        // 1. Provider-scoped DPAPI credential: tts_credential_openai-tts
        var key = await base.ResolveCredentialAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(key))
        {
            return key;
        }

        // 2. Graceful fallback to shared openai_api_key in secure storage
        return await _secureStore.GetSecretAsync("openai_api_key", cancellationToken);
    }
}
