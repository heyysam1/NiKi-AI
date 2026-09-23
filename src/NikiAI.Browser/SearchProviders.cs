using System.Text.Json;
using NikiAI.Core.Browser;

namespace NikiAI.Browser;

/// <summary>
/// Search provider implementation using Brave Search.
/// Supported by default in Edge and Brave browser ecosystems.
/// </summary>
public class BraveSearchProvider : IBrowserSearchProvider
{
    public string ProviderName => "Brave Search";

    public string BuildSearchUrl(string query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        return $"https://search.brave.com/search?q={Uri.EscapeDataString(query.Trim())}";
    }

    public string GetExtractionScript(int maxResults)
    {
        var count = Math.Clamp(maxResults, 1, 10);
        return $$"""
        (() => {
            const results = [];
            // Brave search result selector queries
            const cards = document.querySelectorAll('.snippet[data-type="web"], .search-result, .snippet, div[data-type="web"]');
            for (const card of cards) {
                if (results.length >= {{count}}) break;
                const titleElem = card.querySelector('a.title, .snippet-title, h2 a, a[href]');
                const descElem = card.querySelector('.snippet-description, .snippet-content, .desc, p');
                const linkElem = titleElem || card.querySelector('a[href]');
                
                const title = (titleElem ? titleElem.innerText : '').trim();
                const url = (linkElem ? linkElem.href : '').trim();
                const snippet = (descElem ? descElem.innerText : '').trim();
                
                if (title && url && !url.startsWith('javascript:')) {
                    results.push({ title, url, snippet });
                }
            }
            return JSON.stringify(results);
        })()
        """;
    }

    public IReadOnlyList<BrowserSearchResult> ParseResults(string? scriptOutput)
    {
        if (string.IsNullOrWhiteSpace(scriptOutput))
        {
            return Array.Empty<BrowserSearchResult>();
        }

        try
        {
            using var doc = JsonDocument.Parse(scriptOutput);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<BrowserSearchResult>();
            }

            var list = new List<BrowserSearchResult>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var title = item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                var url = item.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                var snippet = item.TryGetProperty("snippet", out var s) ? s.GetString() ?? "" : "";

                if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(url))
                {
                    list.Add(new BrowserSearchResult(title, url, snippet));
                }
            }

            return list.AsReadOnly();
        }
        catch
        {
            return Array.Empty<BrowserSearchResult>();
        }
    }
}

/// <summary>
/// Search provider implementation using DuckDuckGo HTML.
/// Lightweight, fast, and does not require JavaScript hydration.
/// </summary>
public class DuckDuckGoSearchProvider : IBrowserSearchProvider
{
    public string ProviderName => "DuckDuckGo";

    public string BuildSearchUrl(string query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        return $"https://html.duckduckgo.com/html/?q={Uri.EscapeDataString(query.Trim())}";
    }

    public string GetExtractionScript(int maxResults)
    {
        var count = Math.Clamp(maxResults, 1, 10);
        return $$"""
        (() => {
            const results = [];
            const rows = document.querySelectorAll('.result__body, .result, .web-result');
            for (const row of rows) {
                if (results.length >= {{count}}) break;
                const titleElem = row.querySelector('.result__title a, .result__a, h2 a');
                const snippetElem = row.querySelector('.result__snippet, .snippet');
                
                const title = (titleElem ? titleElem.innerText : '').trim();
                const url = (titleElem ? titleElem.href : '').trim();
                const snippet = (snippetElem ? snippetElem.innerText : '').trim();
                
                if (title && url) {
                    results.push({ title, url, snippet });
                }
            }
            return JSON.stringify(results);
        })()
        """;
    }

    public IReadOnlyList<BrowserSearchResult> ParseResults(string? scriptOutput)
    {
        if (string.IsNullOrWhiteSpace(scriptOutput))
        {
            return Array.Empty<BrowserSearchResult>();
        }

        try
        {
            using var doc = JsonDocument.Parse(scriptOutput);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<BrowserSearchResult>();
            }

            var list = new List<BrowserSearchResult>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var title = item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                var url = item.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                var snippet = item.TryGetProperty("snippet", out var s) ? s.GetString() ?? "" : "";

                if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(url))
                {
                    list.Add(new BrowserSearchResult(title, url, snippet));
                }
            }

            return list.AsReadOnly();
        }
        catch
        {
            return Array.Empty<BrowserSearchResult>();
        }
    }
}

/// <summary>
/// Deterministic mock search provider for offline and reproducible testing.
/// </summary>
public class DeterministicMockSearchProvider : IBrowserSearchProvider
{
    private readonly Func<string, int, IReadOnlyList<BrowserSearchResult>>? _resultFactory;

    public string ProviderName => "Deterministic Test Search";

    public DeterministicMockSearchProvider(Func<string, int, IReadOnlyList<BrowserSearchResult>>? resultFactory = null)
    {
        _resultFactory = resultFactory;
    }

    public string BuildSearchUrl(string query)
    {
        return $"about:blank?q={Uri.EscapeDataString(query)}";
    }

    public string GetExtractionScript(int maxResults)
    {
        return "JSON.stringify([])";
    }

    public IReadOnlyList<BrowserSearchResult> ParseResults(string? scriptOutput)
    {
        return Array.Empty<BrowserSearchResult>();
    }

    public IReadOnlyList<BrowserSearchResult> GenerateDirectResults(string query, int maxResults)
    {
        if (_resultFactory != null)
        {
            return _resultFactory(query, maxResults);
        }

        var results = new List<BrowserSearchResult>();
        var count = Math.Clamp(maxResults, 1, 10);
        for (int i = 1; i <= count; i++)
        {
            results.Add(new BrowserSearchResult(
                Title: $"{query} Reference #{i}",
                Url: $"https://learn.microsoft.com/en-us/search/?q={Uri.EscapeDataString(query)}#{i}",
                Snippet: $"Deterministic reference information and overview for '{query}' (Result #{i})."));
        }
        return results.AsReadOnly();
    }
}
