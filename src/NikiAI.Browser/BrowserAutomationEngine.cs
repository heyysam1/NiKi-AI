using System.Text.Json;
using NikiAI.Core.Browser;

namespace NikiAI.Browser;

/// <summary>
/// High-level browser automation engine orchestrating browser automation.
/// Implements search, page reading, and structured field extraction.
/// Treats all external webpage content as untrusted data.
/// </summary>
public class BrowserAutomationEngine : IBrowserAutomationEngine
{
    private readonly IBrowserDriver _driver;
    private readonly IBrowserSearchProvider _searchProvider;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _isDisposed;

    public SupportedBrowser ActiveBrowser => _driver.BrowserType;

    public BrowserAutomationEngine(
        IBrowserDriver driver,
        IBrowserSearchProvider? searchProvider = null)
    {
        _driver = driver ?? throw new ArgumentNullException(nameof(driver));
        _searchProvider = searchProvider ?? new BraveSearchProvider();
    }

    public static BrowserAutomationEngine Create(
        IBrowserService browserService,
        SupportedBrowser preferredBrowser = SupportedBrowser.Edge,
        IBrowserSearchProvider? searchProvider = null)
    {
        var driver = new CdpBrowserDriver(browserService, preferredBrowser);
        return new BrowserAutomationEngine(driver, searchProvider);
    }

    public async Task<BrowserSearchOutput> SearchAsync(
        string query,
        int maxResults = 5,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("Search query cannot be empty or whitespace.", nameof(query));
        }

        var trimmedQuery = query.Trim();
        cancellationToken.ThrowIfCancellationRequested();
        var count = Math.Clamp(maxResults, 1, 10);

        // Deterministic mock provider optimization for tests/offline
        if (_searchProvider is DeterministicMockSearchProvider mockProvider)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directResults = mockProvider.GenerateDirectResults(trimmedQuery, count);
            return new BrowserSearchOutput(
                Query: trimmedQuery,
                Results: directResults,
                TotalCount: directResults.Count,
                Provider: mockProvider.ProviderName,
                IsUntrustedExternalData: true);
        }

        cancellationToken.ThrowIfCancellationRequested();
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var searchUrl = _searchProvider.BuildSearchUrl(trimmedQuery);
            await _driver.NavigateAsync(searchUrl, TimeSpan.FromSeconds(20), cancellationToken);

            var script = _searchProvider.GetExtractionScript(count);
            var rawJson = await _driver.EvaluateScriptAsync<string>(script, TimeSpan.FromSeconds(10), cancellationToken);

            var results = _searchProvider.ParseResults(rawJson);
            return new BrowserSearchOutput(
                Query: trimmedQuery,
                Results: results,
                TotalCount: results.Count,
                Provider: _searchProvider.ProviderName,
                IsUntrustedExternalData: true);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<BrowserPageContent> ReadPageAsync(
        string url,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ValidateAndNormalizeUrl(url);

        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(25);
        await _lock.WaitAsync(cancellationToken);
        try
        {
            await _driver.NavigateAsync(url, effectiveTimeout, cancellationToken);

            const string readScript = """
            (() => {
                const title = (document.title || '').trim();
                const metaDesc = document.querySelector('meta[name="description"]')?.content || null;
                
                const headings = [];
                for (const h of document.querySelectorAll('h1, h2, h3')) {
                    const text = (h.innerText || '').trim();
                    if (text) {
                        headings.push({ level: h.tagName.toLowerCase(), text });
                    }
                }

                const links = [];
                for (const a of document.querySelectorAll('a[href]')) {
                    const text = (a.innerText || '').trim();
                    const href = (a.href || '').trim();
                    if (text && href && !href.startsWith('javascript:')) {
                        links.push({ text, url: href });
                        if (links.length >= 50) break;
                    }
                }

                const bodyText = (document.body ? document.body.innerText : '').trim();

                return JSON.stringify({
                    title,
                    metaDescription: metaDesc,
                    headings,
                    links,
                    bodyText
                });
            })()
            """;

            var rawJson = await _driver.EvaluateScriptAsync<string>(readScript, TimeSpan.FromSeconds(10), cancellationToken);
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                return new BrowserPageContent(
                    Url: url,
                    Title: string.Empty,
                    Headings: Array.Empty<BrowserHeadingItem>(),
                    Links: Array.Empty<BrowserLinkItem>(),
                    TextContent: string.Empty,
                    MetaDescription: null,
                    IsUntrustedExternalData: true);
            }

            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            var pageTitle = root.TryGetProperty("title", out var tElem) ? tElem.GetString() ?? "" : "";
            var metaDesc = root.TryGetProperty("metaDescription", out var mElem) ? mElem.GetString() : null;
            var bodyText = root.TryGetProperty("bodyText", out var bElem) ? bElem.GetString() ?? "" : "";

            var headingsList = new List<BrowserHeadingItem>();
            if (root.TryGetProperty("headings", out var hArr) && hArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var h in hArr.EnumerateArray())
                {
                    var lvl = h.TryGetProperty("level", out var l) ? l.GetString() ?? "h2" : "h2";
                    var txt = h.TryGetProperty("text", out var tx) ? tx.GetString() ?? "" : "";
                    headingsList.Add(new BrowserHeadingItem(lvl, txt));
                }
            }

            var linksList = new List<BrowserLinkItem>();
            if (root.TryGetProperty("links", out var lArr) && lArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var l in lArr.EnumerateArray())
                {
                    var txt = l.TryGetProperty("text", out var lx) ? lx.GetString() ?? "" : "";
                    var href = l.TryGetProperty("url", out var lu) ? lu.GetString() ?? "" : "";
                    linksList.Add(new BrowserLinkItem(txt, href));
                }
            }

            // Prompt-injection defense: analyze external text for injection indicators
            var combinedText = $"{pageTitle}\n{bodyText}";
            var (hasInjection, flagged) = PromptInjectionDetector.AnalyzeContent(combinedText);
            var envelope = PromptInjectionDetector.CreateUntrustedDataEnvelope(url, pageTitle, bodyText);

            return new BrowserPageContent(
                Url: url,
                Title: pageTitle,
                Headings: headingsList.AsReadOnly(),
                Links: linksList.AsReadOnly(),
                TextContent: bodyText,
                MetaDescription: metaDesc,
                IsUntrustedExternalData: true,
                SuspiciousPromptInjectionDetected: hasInjection,
                FlaggedPhrases: flagged,
                SanitizedDataEnvelope: envelope);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<StructuredExtractionResult> ExtractAsync(
        StructuredExtractionRequest request,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateAndNormalizeUrl(request.Url);

        if (request.Fields == null || request.Fields.Count == 0)
        {
            throw new ArgumentException("Extraction request must contain at least one field definition.", nameof(request));
        }

        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(25);
        await _lock.WaitAsync(cancellationToken);
        try
        {
            await _driver.NavigateAsync(request.Url, effectiveTimeout, cancellationToken);

            var pageTitle = await _driver.EvaluateScriptAsync<string>("document.title", TimeSpan.FromSeconds(5), cancellationToken) ?? "";

            var extractedFields = new List<ExtractedFieldValue>();
            var allExtractedText = new List<string>();

            foreach (var field in request.Fields)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(field.FieldName)) continue;

                var selector = field.CssSelector ?? "body";
                var attr = field.AttributeName;
                var isList = field.IsList;

                var script = BuildFieldExtractionScript(selector, attr, isList);
                try
                {
                    var jsonOutput = await _driver.EvaluateScriptAsync<string>(script, TimeSpan.FromSeconds(5), cancellationToken);
                    if (isList)
                    {
                        var listValues = ParseStringList(jsonOutput);
                        extractedFields.Add(new ExtractedFieldValue(
                            FieldName: field.FieldName,
                            Value: null,
                            ListValues: listValues,
                            Success: true));

                        allExtractedText.AddRange(listValues);
                    }
                    else
                    {
                        var singleValue = ParseSingleString(jsonOutput);
                        extractedFields.Add(new ExtractedFieldValue(
                            FieldName: field.FieldName,
                            Value: singleValue,
                            ListValues: null,
                            Success: true));

                        if (!string.IsNullOrEmpty(singleValue))
                        {
                            allExtractedText.Add(singleValue);
                        }
                    }
                }
                catch (Exception ex)
                {
                    extractedFields.Add(new ExtractedFieldValue(
                        FieldName: field.FieldName,
                        Value: null,
                        ListValues: null,
                        Success: false,
                        ErrorMessage: ex.Message));
                }
            }

            var combinedText = string.Join("\n", allExtractedText);
            var (hasInjection, _) = PromptInjectionDetector.AnalyzeContent(combinedText);

            return new StructuredExtractionResult(
                Url: request.Url,
                Title: pageTitle,
                Fields: extractedFields.AsReadOnly(),
                Success: extractedFields.All(f => f.Success),
                ErrorMessage: null,
                IsUntrustedExternalData: true,
                SuspiciousPromptInjectionDetected: hasInjection);
        }
        finally
        {
            _lock.Release();
        }
    }

    private static string BuildFieldExtractionScript(string selector, string? attributeName, bool isList)
    {
        var escapedSelector = selector.Replace("\"", "\\\"");
        var escapedAttr = attributeName?.Replace("\"", "\\\"");

        if (isList)
        {
            if (string.IsNullOrWhiteSpace(escapedAttr))
            {
                return $$"""
                (() => {
                    const elems = document.querySelectorAll("{{escapedSelector}}");
                    const arr = [];
                    for (const e of elems) {
                        const txt = (e.innerText || '').trim();
                        if (txt) arr.push(txt);
                    }
                    return JSON.stringify(arr);
                })()
                """;
            }

            return $$"""
            (() => {
                const elems = document.querySelectorAll("{{escapedSelector}}");
                const arr = [];
                for (const e of elems) {
                    const attr = (e.getAttribute("{{escapedAttr}}") || '').trim();
                    if (attr) arr.push(attr);
                }
                return JSON.stringify(arr);
            })()
            """;
        }

        if (string.IsNullOrWhiteSpace(escapedAttr))
        {
            return $$"""
            (() => {
                const e = document.querySelector("{{escapedSelector}}");
                return e ? JSON.stringify((e.innerText || '').trim()) : JSON.stringify(null);
            })()
            """;
        }

        return $$"""
        (() => {
            const e = document.querySelector("{{escapedSelector}}");
            return e ? JSON.stringify((e.getAttribute("{{escapedAttr}}") || '').trim()) : JSON.stringify(null);
        })()
        """;
    }

    private static IReadOnlyList<string> ParseStringList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return Array.Empty<string>();
            var list = new List<string>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var s = item.GetString();
                if (!string.IsNullOrEmpty(s)) list.Add(s);
            }
            return list.AsReadOnly();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static string? ParseSingleString(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.String ? doc.RootElement.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    private static void ValidateAndNormalizeUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentException("URL cannot be empty or whitespace.", nameof(url));
        }

        if (url.StartsWith("chrome://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("edge://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("brave://", StringComparison.OrdinalIgnoreCase))
        {
            throw new BlockedNavigationException($"Navigation to internal browser URL ('{url}') is prohibited.");
        }

        if (url.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            throw new BlockedNavigationException($"Navigation to URL with scheme in '{url}' is blocked for security.");
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new ArgumentException($"Invalid URL format: '{url}'.", nameof(url));
        }

        if (uri.Scheme != Uri.UriSchemeHttp &&
            uri.Scheme != Uri.UriSchemeHttps &&
            uri.Scheme != Uri.UriSchemeFile &&
            !string.Equals(uri.Scheme, "about", StringComparison.OrdinalIgnoreCase))
        {
            throw new BlockedNavigationException($"Unsupported URI scheme '{uri.Scheme}'. Allowed schemes are http, https, file, about.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        await _driver.DisposeAsync();
        _lock.Dispose();
    }
}
