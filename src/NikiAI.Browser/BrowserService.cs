using NikiAI.Core.Browser;

namespace NikiAI.Browser;

/// <summary>
/// Browser management service for Niki AI.
/// Strictly restricted to Microsoft Edge and Brave browsers.
/// Google Chrome is prohibited and rejected by design.
/// </summary>
public class BrowserService : IBrowserService
{
    private static readonly string[] EdgeCandidatePaths =
    [
        @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
        @"C:\Program Files\Microsoft\Edge\Application\msedge.exe"
    ];

    private static readonly string[] BraveCandidatePaths =
    [
        @"C:\Program Files\BraveSoftware\Brave-Browser\Application\brave.exe",
        @"C:\Program Files (x86)\BraveSoftware\Brave-Browser\Application\brave.exe",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"BraveSoftware\Brave-Browser\Application\brave.exe")
    ];

    public SupportedBrowser CurrentBrowser { get; private set; }

    public BrowserService(SupportedBrowser preferredBrowser = SupportedBrowser.Edge)
    {
        CurrentBrowser = preferredBrowser;
    }

    public bool IsBrowserAvailable(SupportedBrowser browser)
    {
        return browser switch
        {
            SupportedBrowser.Edge => EdgeCandidatePaths.Any(File.Exists),
            SupportedBrowser.Brave => BraveCandidatePaths.Any(File.Exists),
            _ => throw new ArgumentOutOfRangeException(nameof(browser), browser, "Unknown browser.")
        };
    }

    public Task<string> GetBrowserExecutablePathAsync(SupportedBrowser browser, CancellationToken cancellationToken = default)
    {
        var candidates = browser switch
        {
            SupportedBrowser.Edge => EdgeCandidatePaths,
            SupportedBrowser.Brave => BraveCandidatePaths,
            _ => throw new ArgumentOutOfRangeException(nameof(browser), browser, "Unknown browser.")
        };

        var found = candidates.FirstOrDefault(File.Exists);
        if (found != null)
        {
            return Task.FromResult(found);
        }

        throw new FileNotFoundException(
            $"Configured browser '{browser}' was not found on this system. Supported browsers are Microsoft Edge and Brave.");
    }

    public void SetBrowser(SupportedBrowser browser)
    {
        CurrentBrowser = browser;
    }

    /// <summary>
    /// Validates an arbitrary browser name or path against project constraints.
    /// Explicitly rejects any request for Google Chrome.
    /// </summary>
    public static void ValidateRequestedBrowser(string browserIdentifier)
    {
        BrowserGuardrail.AssertNotChrome(browserIdentifier);
    }
}
