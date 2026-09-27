using System.Text.Json;
using NikiAI.Browser;
using NikiAI.Core.Browser;
using NikiAI.Core.Tools;
using NikiAI.Tools;

namespace NikiAI.Browser.Tests;

public class BrowserPageReadTests : IAsyncDisposable
{
    private readonly IBrowserService _browserService = new BrowserService();
    private readonly string _tempDir;
    private readonly List<string> _tempFiles = new();

    public BrowserPageReadTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"NikiAI_PageReadTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    private string CreateTestHtml(string html)
    {
        var filePath = Path.Combine(_tempDir, $"page_{Guid.NewGuid():N}.html");
        File.WriteAllText(filePath, html);
        _tempFiles.Add(filePath);
        return new Uri(filePath).AbsoluteUri;
    }

    [Fact]
    public async Task ReadPageAsync_ValidLocalPage_ExtractsMetadataAndContent()
    {
        var pageUrl = CreateTestHtml("""
        <!DOCTYPE html>
        <html>
        <head>
          <title>Test Page Title</title>
          <meta name="description" content="A test page description">
        </head>
        <body>
          <h1>Main Heading</h1>
          <h2>Subheading 1</h2>
          <p>This is test body text content.</p>
          <a href="https://learn.microsoft.com">Microsoft Learn</a>
          <a href="https://brave.com">Brave Browser</a>
        </body>
        </html>
        """);

        await using var engine = BrowserAutomationEngine.Create(_browserService, SupportedBrowser.Edge);
        var content = await engine.ReadPageAsync(pageUrl);

        Assert.NotNull(content);
        Assert.Equal("Test Page Title", content.Title);
        Assert.Equal("A test page description", content.MetaDescription);
        Assert.True(content.IsUntrustedExternalData);
        Assert.Contains("This is test body text content.", content.TextContent);
        Assert.Equal(2, content.Headings.Count);
        Assert.Equal("h1", content.Headings[0].Level);
        Assert.Equal("Main Heading", content.Headings[0].Text);
        Assert.Equal(2, content.Links.Count);
        Assert.Contains(content.Links, l => l.Text == "Microsoft Learn" && l.Url.Contains("microsoft.com"));
        Assert.NotNull(content.SanitizedDataEnvelope);
        Assert.Contains("<untrusted_external_webpage_content", content.SanitizedDataEnvelope);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-valid-url")]
    [InlineData("ftp://invalid-scheme.com")]
    public async Task ReadPageAsync_InvalidUrl_ThrowsException(string invalidUrl)
    {
        await using var engine = BrowserAutomationEngine.Create(_browserService, SupportedBrowser.Edge);

        await Assert.ThrowsAnyAsync<Exception>(() => engine.ReadPageAsync(invalidUrl));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<h1>Hello</h1>")]
    public async Task ReadPageAsync_BlockedSchemes_ThrowsBlockedNavigationException(string blockedUrl)
    {
        await using var engine = BrowserAutomationEngine.Create(_browserService, SupportedBrowser.Edge);

        await Assert.ThrowsAsync<BlockedNavigationException>(() => engine.ReadPageAsync(blockedUrl));
    }

    [Theory]
    [InlineData("chrome://settings")]
    [InlineData("chrome://flags")]
    [InlineData("chrome-extension://xyz/options.html")]
    public async Task ReadPageAsync_ChromeTargets_ThrowsChromeProhibitedException(string chromeUrl)
    {
        await using var engine = BrowserAutomationEngine.Create(_browserService, SupportedBrowser.Edge);

        await Assert.ThrowsAsync<ChromeProhibitedException>(() => engine.ReadPageAsync(chromeUrl));
    }

    [Fact]
    public async Task ReadPageAsync_MalformedHtml_ExtractsGracefullyWithoutCrashing()
    {
        var malformedUrl = CreateTestHtml("<h1>No html tags</h1><p>Just plain text without closure");

        await using var engine = BrowserAutomationEngine.Create(_browserService, SupportedBrowser.Edge);
        var content = await engine.ReadPageAsync(malformedUrl);

        Assert.NotNull(content);
        Assert.True(content.IsUntrustedExternalData);
        Assert.Contains("No html tags", content.TextContent);
    }

    [Fact]
    public async Task ReadPageAsync_Cancellation_ThrowsOperationCanceledException()
    {
        var pageUrl = CreateTestHtml("<html><body><p>Cancel Test</p></body></html>");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await using var engine = BrowserAutomationEngine.Create(_browserService, SupportedBrowser.Edge);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            engine.ReadPageAsync(pageUrl, cancellationToken: cts.Token));
    }

    [Fact]
    public async Task BrowserPageReadTool_ExecuteAsync_ReturnsValidStructuredJson()
    {
        var pageUrl = CreateTestHtml("<html><head><title>Tool Page</title></head><body><h1>Heading</h1><p>Body</p></body></html>");

        await using var engine = BrowserAutomationEngine.Create(_browserService, SupportedBrowser.Edge);
        var tool = new BrowserPageReadTool(engine);
        var call = new ToolCall("c_read_1", "browser_page_read", JsonSerializer.Serialize(new { url = pageUrl }), DateTimeOffset.UtcNow);

        var result = await tool.ExecuteAsync(call);

        Assert.True(result.IsSuccess);
        using var doc = JsonDocument.Parse(result.OutputJson!);
        var root = doc.RootElement;
        Assert.Equal("Tool Page", root.GetProperty("title").GetString());
        Assert.True(root.GetProperty("is_untrusted_external_data").GetBoolean());
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch { }

        return ValueTask.CompletedTask;
    }
}
