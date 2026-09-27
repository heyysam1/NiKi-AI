namespace NikiAI.Core.Browser;

/// <summary>
/// Supported browsers for Niki AI automation and web tasks.
/// Resolved dynamically via capability-based adapters.
/// </summary>
public enum SupportedBrowser
{
    Edge,
    Brave,
    Chrome,
    Custom
}

/// <summary>
/// Browser options for web navigation and inspection.
/// </summary>
public record BrowserOptions(
    SupportedBrowser PreferredBrowser = SupportedBrowser.Edge,
    bool Headless = false,
    TimeSpan Timeout = default
);

/// <summary>
/// Browser abstraction contract.
/// </summary>
public interface IBrowserService
{
    SupportedBrowser CurrentBrowser { get; }
    bool IsBrowserAvailable(SupportedBrowser browser);
    Task<string> GetBrowserExecutablePathAsync(SupportedBrowser browser, CancellationToken cancellationToken = default);
}

/// <summary>
/// Low-level browser driver contract for controlling browser instances (CDP).
/// </summary>
public interface IBrowserDriver : IAsyncDisposable
{
    SupportedBrowser BrowserType { get; }
    bool IsRunning { get; }
    Task LaunchAsync(CancellationToken cancellationToken = default);
    Task<string> NavigateAsync(string url, TimeSpan timeout, CancellationToken cancellationToken = default);
    Task<T?> EvaluateScriptAsync<T>(string expression, TimeSpan timeout, CancellationToken cancellationToken = default);
    Task CloseAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Search provider interface for parsing and extracting search results via the browser.
/// Decouples search URL format and DOM extraction from the browser engine.
/// </summary>
public interface IBrowserSearchProvider
{
    string ProviderName { get; }
    string BuildSearchUrl(string query);
    string GetExtractionScript(int maxResults);
    IReadOnlyList<BrowserSearchResult> ParseResults(string? scriptOutput);
}

/// <summary>
/// High-level browser automation engine orchestrating browser automation,
/// navigation, reading, search, and structured extraction.
/// </summary>
public interface IBrowserAutomationEngine : IAsyncDisposable
{
    SupportedBrowser ActiveBrowser { get; }
    Task<BrowserSearchOutput> SearchAsync(string query, int maxResults = 5, CancellationToken cancellationToken = default);
    Task<BrowserPageContent> ReadPageAsync(string url, TimeSpan? timeout = null, CancellationToken cancellationToken = default);
    Task<StructuredExtractionResult> ExtractAsync(StructuredExtractionRequest request, TimeSpan? timeout = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Validation utilities enforcing browser constraints.
/// </summary>
public static class BrowserGuardrail
{
    public static SupportedBrowser ParseAndValidate(string browserName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(browserName);

        var trimmed = browserName.Trim();

        if (string.Equals(trimmed, "edge", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "msedge", StringComparison.OrdinalIgnoreCase))
        {
            return SupportedBrowser.Edge;
        }

        if (string.Equals(trimmed, "brave", StringComparison.OrdinalIgnoreCase))
        {
            return SupportedBrowser.Brave;
        }

        if (string.Equals(trimmed, "chrome", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "googlechrome", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "google-chrome", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "google chrome", StringComparison.OrdinalIgnoreCase))
        {
            return SupportedBrowser.Chrome;
        }

        if (string.Equals(trimmed, "custom", StringComparison.OrdinalIgnoreCase))
        {
            return SupportedBrowser.Custom;
        }

        throw new ArgumentException($"Unsupported or unrecognized browser: '{browserName}'.", nameof(browserName));
    }
}
