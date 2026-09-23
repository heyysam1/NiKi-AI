using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NikiAI.Core.Security;
using NikiAI.Core.Voice;

namespace NikiAI.Voice;

/// <summary>
/// Generic HTTP Text-to-Speech provider supporting standard speech synthesis REST endpoints.
/// Decoupled from specific vendors; credentials are resolved using ProviderId via ISecureSettingsStore.
/// </summary>
public class CustomHttpTtsProvider : ITtsProvider
{
    private readonly HttpClient _httpClient;
    private readonly ISecureSettingsStore _secureSettingsStore;
    private readonly ILogger<CustomHttpTtsProvider>? _logger;

    public string ProviderId { get; }
    public string DisplayName { get; }
    public string EndpointUrl { get; }
    public string DefaultModel { get; }
    public string DefaultVoiceId { get; }

    public bool IsAvailable => !string.IsNullOrWhiteSpace(EndpointUrl);

    public CustomHttpTtsProvider(
        HttpClient httpClient,
        ISecureSettingsStore secureSettingsStore,
        string providerId = "custom-tts",
        string displayName = "Custom HTTP TTS",
        string endpointUrl = "https://api.openai.com/v1/audio/speech",
        string defaultModel = "tts-1",
        string defaultVoiceId = "alloy",
        ILogger<CustomHttpTtsProvider>? logger = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _secureSettingsStore = secureSettingsStore ?? throw new ArgumentNullException(nameof(secureSettingsStore));
        ProviderId = string.IsNullOrWhiteSpace(providerId) ? "custom-tts" : providerId;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Custom HTTP TTS" : displayName;
        EndpointUrl = endpointUrl;
        DefaultModel = defaultModel;
        DefaultVoiceId = defaultVoiceId;
        _logger = logger;
    }

    public virtual Task<IReadOnlyList<VoiceDescriptor>> GetVoicesAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<VoiceDescriptor>
        {
            new(DefaultVoiceId, DefaultVoiceId, "Neutral", "en-US", ProviderId)
        };
        return Task.FromResult<IReadOnlyList<VoiceDescriptor>>(list);
    }

    public virtual async Task<Stream> SynthesizeSpeechAsync(string text, string? voiceId = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var apiKey = await ResolveCredentialAsync(cancellationToken);

        var voiceToUse = !string.IsNullOrWhiteSpace(voiceId) ? voiceId : DefaultVoiceId;

        var payload = new
        {
            model = DefaultModel,
            input = text,
            voice = voiceToUse,
            response_format = "wav"
        };

        var json = JsonSerializer.Serialize(payload);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, EndpointUrl);
        httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger?.LogWarning("TTS endpoint '{Endpoint}' returned error code {StatusCode}: {Body}", EndpointUrl, response.StatusCode, errBody);
            throw new HttpRequestException($"TTS request failed with status {response.StatusCode}: {errBody}");
        }

        var memoryStream = new MemoryStream();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await stream.CopyToAsync(memoryStream, cancellationToken);
        memoryStream.Position = 0;

        return memoryStream;
    }

    protected virtual async Task<string?> ResolveCredentialAsync(CancellationToken cancellationToken)
    {
        // 1. Provider-scoped DPAPI credential: tts_credential_{ProviderId}
        var scopedKey = $"tts_credential_{ProviderId}";
        var apiKey = await _secureSettingsStore.GetSecretAsync(scopedKey, cancellationToken);

        // 2. Generic fallback if configured
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            apiKey = await _secureSettingsStore.GetSecretAsync("tts_credential_default", cancellationToken);
        }

        return apiKey;
    }
}
