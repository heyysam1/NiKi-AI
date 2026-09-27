using NikiAI.Core.Browser;

namespace NikiAI.Browser;

/// <summary>
/// Browser management service for Niki AI.
/// Resolves installed browsers and validates capability against registered adapters.
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

    private static readonly string[] ChromeCandidatePaths =
    [
        @"C:\Program Files\Google\Chrome\Application\chrome.exe",
        @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Google\Chrome\Application\chrome.exe")
    ];

    private readonly IBrowserAdapterRegistry? _adapterRegistry;

    public SupportedBrowser CurrentBrowser { get; private set; }

    public BrowserService(
        SupportedBrowser preferredBrowser = SupportedBrowser.Edge,
        IBrowserAdapterRegistry? adapterRegistry = null)
    {
        CurrentBrowser = preferredBrowser;
        _adapterRegistry = adapterRegistry;
    }

    public bool IsBrowserAvailable(SupportedBrowser browser)
    {
        var candidates = GetCandidatesForBrowser(browser);
        var found = candidates.FirstOrDefault(File.Exists);
        if (found == null)
        {
            return false;
        }

        if (_adapterRegistry != null)
        {
            var descriptor = CreateDescriptor(browser, found);
            var adapters = _adapterRegistry.GetRegisteredAdapters();
            return adapters.Any(a => a.CanAutomateAsync(descriptor).GetAwaiter().GetResult());
        }

        return true;
    }

    public async Task<string> GetBrowserExecutablePathAsync(SupportedBrowser browser, CancellationToken cancellationToken = default)
    {
        var candidates = GetCandidatesForBrowser(browser);
        var found = candidates.FirstOrDefault(File.Exists);
        if (found == null)
        {
            throw new BrowserUnavailableException(
                $"Configured browser '{browser}' was not found on this system.");
        }

        if (_adapterRegistry != null)
        {
            var descriptor = CreateDescriptor(browser, found);
            var adapter = await _adapterRegistry.ResolveAdapterAsync(descriptor, cancellationToken);
            if (adapter == null)
            {
                throw new BrowserUnavailableException(
                    $"No compatible browser adapter found for '{browser}' at '{found}'.");
            }
        }

        return found;
    }

    public void SetBrowser(SupportedBrowser browser)
    {
        CurrentBrowser = browser;
    }

    public static BrowserDescriptor CreateDescriptor(SupportedBrowser browser, string executablePath)
    {
        return browser switch
        {
            SupportedBrowser.Edge => new BrowserDescriptor("edge", "Microsoft Edge", executablePath, BrowserFamily.Chromium, "chromium-cdp"),
            SupportedBrowser.Brave => new BrowserDescriptor("brave", "Brave Browser", executablePath, BrowserFamily.Chromium, "chromium-cdp"),
            SupportedBrowser.Chrome => new BrowserDescriptor("chrome", "Google Chrome", executablePath, BrowserFamily.Chromium, "chromium-cdp"),
            _ => new BrowserDescriptor("custom", "Custom Browser", executablePath, BrowserFamily.Custom)
        };
    }

    public static void ValidateRequestedBrowser(string browserIdentifier)
    {
        if (string.IsNullOrWhiteSpace(browserIdentifier))
        {
            return;
        }

        BrowserGuardrail.ParseAndValidate(browserIdentifier);
    }

    private static IReadOnlyList<string> GetCandidatesForBrowser(SupportedBrowser browser)
    {
        return browser switch
        {
            SupportedBrowser.Edge => EdgeCandidatePaths,
            SupportedBrowser.Brave => BraveCandidatePaths,
            SupportedBrowser.Chrome => ChromeCandidatePaths,
            _ => Array.Empty<string>()
        };
    }
}
