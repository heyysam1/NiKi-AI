namespace NikiAI.Core.Browser;

/// <summary>
/// Supported browsers for Niki AI automation and web tasks.
/// Google Chrome is strictly prohibited per project specifications.
/// </summary>
public enum SupportedBrowser
{
    Edge,
    Brave,
    Chrome,
    Custom
}

/// <summary>
/// Exception thrown whenever an attempt is made to request, select, launch, or configure Google Chrome.
/// Google Chrome is not installed on the target system and is strictly prohibited.
/// </summary>
public class ChromeProhibitedException : InvalidOperationException
{
    public ChromeProhibitedException() 
        : base("Google Chrome is strictly prohibited. Niki AI only supports Microsoft Edge or Brave.")
    {
    }

    public ChromeProhibitedException(string message) 
        : base(message)
    {
    }
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
/// High-level browser automation engine orchestrating Edge/Brave automation,
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
    public static void AssertNotChrome(string? browserNameOrPath)
    {
        if (string.IsNullOrWhiteSpace(browserNameOrPath))
        {
            return;
        }

        if (browserNameOrPath.Contains("chrome", StringComparison.OrdinalIgnoreCase))
        {
            throw new ChromeProhibitedException(
                $"Google Chrome is strictly prohibited. Access to ('{browserNameOrPath}') was blocked. Only Microsoft Edge and Brave are supported.");
        }
    }

    public static SupportedBrowser ParseAndValidate(string browserName)
    {
        AssertNotChrome(browserName);

        if (string.Equals(browserName, "edge", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(browserName, "msedge", StringComparison.OrdinalIgnoreCase))
        {
            return SupportedBrowser.Edge;
        }

        if (string.Equals(browserName, "brave", StringComparison.OrdinalIgnoreCase))
        {
            return SupportedBrowser.Brave;
        }

        throw new ArgumentException($"Unsupported browser: '{browserName}'. Supported browsers are Edge and Brave.", nameof(browserName));
    }
}
