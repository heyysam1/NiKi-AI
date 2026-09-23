namespace NikiAI.Core.Vision;

/// <summary>
/// Target scope for on-demand screen capture.
/// Reuses Phase 9 spatial and window concepts.
/// </summary>
public enum ScreenTargetScope
{
    PrimaryScreen,
    ActiveWindow,
    CustomRegion
}

/// <summary>
/// Result of an on-demand screen capture operation.
/// Raw image bytes are strictly ephemeral in-memory buffers.
/// </summary>
public record ScreenCaptureResult(
    bool Success,
    byte[]? ImageBytes,
    int Width,
    int Height,
    DateTimeOffset Timestamp,
    string SourceDescription,
    string? ErrorMessage = null,
    bool IsSyntheticFallback = false
)
{
    public static ScreenCaptureResult Succeeded(byte[] bytes, int width, int height, string source, bool isSyntheticFallback = false) =>
        new(true, bytes, width, height, DateTimeOffset.UtcNow, source, null, isSyntheticFallback);

    public static ScreenCaptureResult Failed(string error, string source = "Unknown") =>
        new(false, null, 0, 0, DateTimeOffset.UtcNow, source, error, false);
}

/// <summary>
/// Request for vision analysis of captured screen data.
/// </summary>
public record VisionAnalysisRequest(
    byte[] ImageBytes,
    string Prompt,
    string? TargetDescription = null,
    string? Model = null
);

/// <summary>
/// Structured observation result from vision model analysis.
/// Always marked with ContainsUntrustedContent = true as external screen content is untrusted.
/// </summary>
public record VisionAnalysisResult(
    bool Success,
    string Summary,
    string? ExtractedText = null,
    IReadOnlyList<string>? DetectedElements = null,
    string? ErrorMessage = null,
    bool ContainsUntrustedContent = true
)
{
    public static VisionAnalysisResult Succeeded(string summary, string? extractedText = null, IReadOnlyList<string>? detectedElements = null) =>
        new(true, summary, extractedText, detectedElements, null, true);

    public static VisionAnalysisResult Failed(string errorMessage) =>
        new(false, string.Empty, null, null, errorMessage, true);
}
