using Microsoft.Extensions.Logging;
using NikiAI.Core.Security;

namespace NikiAI.Agent;

/// <summary>
/// OpenAI implementation of IVisionProvider targeting GPT-4o vision.
/// Resolves API credentials using provider-scoped key 'vision_credential_openai-vision',
/// with graceful fallback to 'openai_api_key' if previously configured.
/// </summary>
public class OpenAiVisionProvider : CustomHttpVisionProvider
{
    public const string DefaultProviderId = "openai-vision";
    private readonly ISecureSettingsStore _secureStore;

    public OpenAiVisionProvider(
        HttpClient httpClient,
        ISecureSettingsStore secureSettingsStore,
        string endpointUrl = "https://api.openai.com/v1/chat/completions",
        string defaultModel = "gpt-4o",
        ILogger<OpenAiVisionProvider>? logger = null)
        : base(
            httpClient,
            secureSettingsStore,
            providerId: DefaultProviderId,
            displayName: "OpenAI GPT-4o Vision",
            endpointUrl: endpointUrl,
            defaultModel: defaultModel)
    {
        _secureStore = secureSettingsStore ?? throw new ArgumentNullException(nameof(secureSettingsStore));
    }

    protected override async Task<string?> ResolveCredentialAsync(CancellationToken cancellationToken)
    {
        // 1. Check provider-scoped DPAPI credential: vision_credential_openai-vision
        var key = await base.ResolveCredentialAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(key))
        {
            return key;
        }

        // 2. Graceful fallback to shared openai_api_key in secure storage
        return await _secureStore.GetSecretAsync("openai_api_key", cancellationToken);
    }
}
