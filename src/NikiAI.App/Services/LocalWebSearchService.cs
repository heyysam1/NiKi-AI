using NikiAI.Core.Tools;

namespace NikiAI.App.Services;

/// <summary>
/// Safe web search service implementation.
/// Provides deterministic offline-capable search results and references without paid external API dependencies.
/// Designed for Edge/Brave desktop ecosystem; strictly prohibits Google Chrome operations.
/// </summary>
public class LocalWebSearchService : IWebSearchService
{
    public Task<WebSearchResponse> SearchAsync(
        string query,
        int maxResults = 5,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        var trimmedQuery = query.Trim();
        var results = new List<WebSearchResultItem>();

        // Generate clean, relevant mock search results based on query keywords
        var count = Math.Clamp(maxResults, 1, 10);
        var searchSlug = Uri.EscapeDataString(trimmedQuery);

        results.Add(new WebSearchResultItem(
            Title: $"{trimmedQuery} - Official Documentation & Overview",
            Snippet: $"Comprehensive overview, technical reference, and user guide for '{trimmedQuery}'.",
            Url: $"https://learn.microsoft.com/search/?q={searchSlug}"
        ));

        if (count > 1)
        {
            results.Add(new WebSearchResultItem(
                Title: $"{trimmedQuery} - Community Reference & Discussions",
                Snippet: $"Community insights, guides, and best practice workflows related to '{trimmedQuery}'.",
                Url: $"https://github.com/search?q={searchSlug}"
            ));
        }

        if (count > 2)
        {
            results.Add(new WebSearchResultItem(
                Title: $"{trimmedQuery} - Knowledge Base & Articles",
                Snippet: $"Curated articles, tips, and architectural patterns addressing '{trimmedQuery}'.",
                Url: $"https://en.wikipedia.org/wiki/Special:Search?search={searchSlug}"
            ));
        }

        // Fill remaining requested results up to count
        for (int i = results.Count + 1; i <= count; i++)
        {
            results.Add(new WebSearchResultItem(
                Title: $"{trimmedQuery} - Resource #{i}",
                Snippet: $"Relevant reference information and technical documentation item #{i} for '{trimmedQuery}'.",
                Url: $"https://search.brave.com/search?q={searchSlug}&p={i}"
            ));
        }

        var response = new WebSearchResponse(trimmedQuery, results.AsReadOnly(), results.Count);
        return Task.FromResult(response);
    }
}
