using System.Text.Json;
using NikiAI.Browser;
using NikiAI.Core.Browser;
using NikiAI.Core.Tools;
using NikiAI.Tools;

namespace NikiAI.Browser.Tests;

public class BrowserSearchTests
{
    private readonly IBrowserService _browserService = new BrowserService();

    [Fact]
    public async Task SearchAsync_ValidQuery_ReturnsStructuredResults()
    {
        var mockProvider = new DeterministicMockSearchProvider();
        await using var engine = new BrowserAutomationEngine(
            new CdpBrowserDriver(_browserService, SupportedBrowser.Edge),
            mockProvider);

        var output = await engine.SearchAsync("C# pattern matching", maxResults: 3);

        Assert.NotNull(output);
        Assert.Equal("C# pattern matching", output.Query);
        Assert.True(output.IsUntrustedExternalData);
        Assert.Equal(3, output.Results.Count);
        Assert.All(output.Results, r =>
        {
            Assert.False(string.IsNullOrWhiteSpace(r.Title));
            Assert.False(string.IsNullOrWhiteSpace(r.Url));
            Assert.False(string.IsNullOrWhiteSpace(r.Snippet));
        });
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task SearchAsync_EmptyOrWhitespaceQuery_ThrowsArgumentException(string? invalidQuery)
    {
        var mockProvider = new DeterministicMockSearchProvider();
        await using var engine = new BrowserAutomationEngine(
            new CdpBrowserDriver(_browserService, SupportedBrowser.Edge),
            mockProvider);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            engine.SearchAsync(invalidQuery!, maxResults: 5));
    }

    [Theory]
    [InlineData("install chrome")]
    [InlineData("download chrome browser")]
    [InlineData("automate chrome")]
    [InlineData("chrome.exe")]
    public async Task SearchAsync_ChromeQuery_ExecutesSuccessfullyAsStandardQuery(string chromeQuery)
    {
        var mockProvider = new DeterministicMockSearchProvider();
        await using var engine = new BrowserAutomationEngine(
            new CdpBrowserDriver(_browserService, SupportedBrowser.Edge),
            mockProvider);

        var output = await engine.SearchAsync(chromeQuery, maxResults: 5);
        Assert.NotNull(output);
        Assert.Equal(chromeQuery, output.Query);
        Assert.NotEmpty(output.Results);
    }

    [Fact]
    public async Task SearchAsync_WhenCancelled_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var mockProvider = new DeterministicMockSearchProvider();
        await using var engine = new BrowserAutomationEngine(
            new CdpBrowserDriver(_browserService, SupportedBrowser.Edge),
            mockProvider);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            engine.SearchAsync("valid query", 5, cts.Token));
    }

    [Fact]
    public async Task BrowserSearchTool_ExecuteAsync_ValidCall_ReturnsSuccessfulJson()
    {
        var mockProvider = new DeterministicMockSearchProvider();
        await using var engine = new BrowserAutomationEngine(
            new CdpBrowserDriver(_browserService, SupportedBrowser.Edge),
            mockProvider);

        var tool = new BrowserSearchTool(engine);
        var call = new ToolCall("c_srch_1", "browser_search", """{"query": "async await", "max_results": 2}""", DateTimeOffset.UtcNow);

        var result = await tool.ExecuteAsync(call);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.OutputJson);

        using var doc = JsonDocument.Parse(result.OutputJson);
        var root = doc.RootElement;
        Assert.Equal("async await", root.GetProperty("query").GetString());
        Assert.True(root.GetProperty("is_untrusted_external_data").GetBoolean());
        Assert.Equal(2, root.GetProperty("results").GetArrayLength());
    }

    [Fact]
    public async Task BrowserSearchTool_ExecuteAsync_ChromeQuery_ReturnsPolicyFailure()
    {
        var mockProvider = new DeterministicMockSearchProvider();
        await using var engine = new BrowserAutomationEngine(
            new CdpBrowserDriver(_browserService, SupportedBrowser.Edge),
            mockProvider);

        var tool = new BrowserSearchTool(engine);
        var call = new ToolCall("c_srch_2", "browser_search", """{"query": "download Google Chrome"}""", DateTimeOffset.UtcNow);

        var result = await tool.ExecuteAsync(call);

        Assert.False(result.IsSuccess);
        Assert.Contains("Google Chrome operations are prohibited", result.ErrorMessage);
    }
}
