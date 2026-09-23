using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NikiAI.Core.Security;
using NikiAI.Core.Vision;

namespace NikiAI.Agent;

/// <summary>
/// Generic HTTP vision provider supporting any OpenAI-compatible multimodal endpoint.
/// Decoupled from specific AI vendors and supports custom local or remote gateways.
/// Credentials are provider-scoped and resolved via ISecureSettingsStore.
/// </summary>
public class CustomHttpVisionProvider : IVisionProvider
{
    private readonly HttpClient _httpClient;
    private readonly ISecureSettingsStore _secureSettingsStore;
    private readonly ILogger<CustomHttpVisionProvider>? _logger;

    public string ProviderId { get; }
    public string DisplayName { get; }
    public string EndpointUrl { get; }
    public string DefaultModel { get; }

    public bool IsAvailable => !string.IsNullOrWhiteSpace(EndpointUrl);

    public CustomHttpVisionProvider(
        HttpClient httpClient,
        ISecureSettingsStore secureSettingsStore,
        string providerId = "custom-vision",
        string displayName = "Custom HTTP Vision",
        string endpointUrl = "https://api.openai.com/v1/chat/completions",
        string defaultModel = "gpt-4o",
        ILogger<CustomHttpVisionProvider>? logger = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _secureSettingsStore = secureSettingsStore ?? throw new ArgumentNullException(nameof(secureSettingsStore));
        ProviderId = string.IsNullOrWhiteSpace(providerId) ? "custom-vision" : providerId;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Custom HTTP Vision" : displayName;
        EndpointUrl = endpointUrl;
        DefaultModel = defaultModel;
        _logger = logger;
    }

    public virtual async Task<VisionAnalysisResult> AnalyzeImageAsync(VisionAnalysisRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.ImageBytes == null || request.ImageBytes.Length == 0)
        {
            return VisionAnalysisResult.Failed("Image data is empty.");
        }

        try
        {
            // Provider-scoped credential resolution
            var apiKey = await ResolveCredentialAsync(cancellationToken);

            var base64Image = Convert.ToBase64String(request.ImageBytes);
            var modelToUse = !string.IsNullOrWhiteSpace(request.Model) ? request.Model : DefaultModel;

            var payload = new
            {
                model = modelToUse,
                messages = new object[]
                {
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new { type = "text", text = request.Prompt },
                            new
                            {
                                type = "image_url",
                                image_url = new
                                {
                                    url = $"data:image/png;base64,{base64Image}"
                                }
                            }
                        }
                    }
                },
                max_tokens = 1000
            };

            var jsonContent = JsonSerializer.Serialize(payload);
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, EndpointUrl);
            httpRequest.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            }

            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger?.LogWarning("Vision API returned non-success code {StatusCode}: {Body}", response.StatusCode, responseBody);
                return VisionAnalysisResult.Failed($"Vision API error {(int)response.StatusCode}: {response.ReasonPhrase}");
            }

            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var firstChoice = choices[0];
                if (firstChoice.TryGetProperty("message", out var message) &&
                    message.TryGetProperty("content", out var contentElem))
                {
                    var content = contentElem.GetString() ?? string.Empty;
                    return VisionAnalysisResult.Succeeded(content);
                }
            }

            return VisionAnalysisResult.Failed("Vision API returned an unexpected response structure.");
        }
        catch (OperationCanceledException)
        {
            return VisionAnalysisResult.Failed("Vision analysis was cancelled.");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Exception during vision analysis request to {Endpoint}", EndpointUrl);
            return VisionAnalysisResult.Failed($"Vision request error: {ex.Message}");
        }
    }

    protected virtual async Task<string?> ResolveCredentialAsync(CancellationToken cancellationToken)
    {
        // 1. Check provider-scoped DPAPI credential: vision_credential_{ProviderId}
        var scopedKey = $"vision_credential_{ProviderId}";
        var apiKey = await _secureSettingsStore.GetSecretAsync(scopedKey, cancellationToken);

        // 2. Fallback to generic vision credential if specific one isn't present
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            apiKey = await _secureSettingsStore.GetSecretAsync("vision_credential_default", cancellationToken);
        }

        return apiKey;
    }
}
