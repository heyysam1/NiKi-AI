using NikiAI.Core.Vision;

namespace NikiAI.Agent;

/// <summary>
/// Deterministic offline vision provider for testing, verification, and offline environments.
/// Does not make any external network calls.
/// </summary>
public class MockVisionProvider : IVisionProvider
{
    public const string DefaultProviderId = "mock-vision";

    public string ProviderId { get; set; } = DefaultProviderId;
    public string DisplayName { get; set; } = "Offline Mock Vision Provider";
    public bool IsAvailable { get; set; } = true;

    /// <summary>
    /// Optional preset result to return. If null, a deterministic response is generated based on the request.
    /// </summary>
    public VisionAnalysisResult? PresetResult { get; set; }

    /// <summary>
    /// Records the last received request for test verification.
    /// </summary>
    public VisionAnalysisRequest? LastRequest { get; private set; }

    /// <summary>
    /// Tracks invocation count for verification.
    /// </summary>
    public int InvocationCount { get; private set; }

    public Task<VisionAnalysisResult> AnalyzeImageAsync(VisionAnalysisRequest request, CancellationToken cancellationToken = default)
    {
        InvocationCount++;
        LastRequest = request;

        if (PresetResult != null)
        {
            return Task.FromResult(PresetResult);
        }

        var summary = $"Analyzed {request.ImageBytes.Length} bytes for prompt: '{request.Prompt}' (Target: {request.TargetDescription ?? "General"}).";
        var elements = new List<string> { "Window Frame", "Content View", "Action Button" };
        var result = VisionAnalysisResult.Succeeded(
            summary: summary,
            extractedText: "Mock Window Title - OK",
            detectedElements: elements
        );

        return Task.FromResult(result);
    }
}
