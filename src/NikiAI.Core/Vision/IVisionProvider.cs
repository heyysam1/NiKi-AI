namespace NikiAI.Core.Vision;

/// <summary>
/// Universal provider contract for multimodal vision analysis.
/// Decoupled from specific AI vendors (OpenAI, Gemini, custom HTTP).
/// Credentials are resolved in implementations using ProviderId.
/// </summary>
public interface IVisionProvider
{
    /// <summary>
    /// Unique provider identifier (e.g. "openai-vision", "custom-vision", "mock-vision").
    /// Used for provider selection and provider-scoped DPAPI credential lookup.
    /// </summary>
    string ProviderId { get; }

    /// <summary>
    /// User-friendly display name.
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Indicates whether the provider is configured and available for analysis.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Performs visual analysis of the provided image bytes with the specified prompt.
    /// </summary>
    Task<VisionAnalysisResult> AnalyzeImageAsync(VisionAnalysisRequest request, CancellationToken cancellationToken = default);
}
