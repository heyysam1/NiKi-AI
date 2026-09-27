using NikiAI.Core.Tools;
using NikiAI.Tools;
using Xunit;

namespace NikiAI.Tools.Tests;

public class SearchWebToolTests
{
    private class MockSearchService : IWebSearchService
    {
        public string? LastQuery { get; private set; }
        public int LastMaxResults { get; private set; }

        public Task<WebSearchResponse> SearchAsync(
            string query,
            int maxResults = 5,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastQuery = query;
            LastMaxResults = maxResults;

            var items = new List<WebSearchResultItem>
            {
                new("Result 1", "Snippet 1", "https://example.com/1"),
                new("Result 2", "Snippet 2", "https://example.com/2")
            };

            return Task.FromResult(new WebSearchResponse(query, items.AsReadOnly(), items.Count));
        }
    }

    private readonly MockSearchService _searchService = new();

    [Fact]
    public async Task ExecuteAsync_ValidQuery_ReturnsWebResults()
    {
        var tool = new SearchWebTool(_searchService);
        var call = new ToolCall("s1", "search_web", """{ "query": "WPF design patterns", "max_results": 3 }""", DateTimeOffset.UtcNow);

        var result = await tool.ExecuteAsync(call);

        Assert.True(result.IsSuccess);
        Assert.Equal("WPF design patterns", _searchService.LastQuery);
        Assert.Equal(3, _searchService.LastMaxResults);
        Assert.Contains("Result 1", result.OutputJson);
    }

    [Fact]
    public async Task ExecuteAsync_ChromeAutomationQuery_Rejected()
    {
        var tool = new SearchWebTool(_searchService);
        var call = new ToolCall("s2", "search_web", """{ "query": "how to automate chrome in python" }""", DateTimeOffset.UtcNow);

        var result = await tool.ExecuteAsync(call);

        Assert.False(result.IsSuccess);
        Assert.Contains("Google Chrome operations are prohibited", result.ErrorMessage);
        Assert.Null(_searchService.LastQuery);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyQuery_ReturnsFailure()
    {
        var tool = new SearchWebTool(_searchService);
        var call = new ToolCall("s3", "search_web", """{ "query": "   " }""", DateTimeOffset.UtcNow);

        var result = await tool.ExecuteAsync(call);

        Assert.False(result.IsSuccess);
        Assert.Contains("cannot be empty", result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_Cancellation_ReturnsCancelledFailure()
    {
        var tool = new SearchWebTool(_searchService);
        var call = new ToolCall("s4", "search_web", """{ "query": "test query" }""", DateTimeOffset.UtcNow);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await tool.ExecuteAsync(call, cts.Token);

        Assert.False(result.IsSuccess);
        Assert.Contains("cancelled", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }
}
