using System.Text.Json;
using NikiAI.Browser;
using NikiAI.Core.Browser;
using NikiAI.Core.Tools;
using NikiAI.Tools;

namespace NikiAI.Browser.Tests;

public class BrowserStructuredExtractionTests : IAsyncDisposable
{
    private readonly IBrowserService _browserService = new BrowserService();
    private readonly string _tempDir;
    private readonly List<string> _tempFiles = new();

    public BrowserStructuredExtractionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"NikiAI_ExtractTests_{Guid.NewGuid():N}");
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
    public async Task ExtractAsync_ValidFieldsAndAttributes_ReturnsStructuredValues()
    {
        var pageUrl = CreateTestHtml("""
        <!DOCTYPE html>
        <html>
        <head><title>Product Catalog</title></head>
        <body>
          <h1 id="product-name">Widget Pro 2000</h1>
          <span class="price">$49.99</span>
          <a class="cta-link" href="https://example.com/buy">Purchase Now</a>
          <ul class="features">
            <li class="item">Fast</li>
            <li class="item">Reliable</li>
            <li class="item">Secure</li>
          </ul>
        </body>
        </html>
        """);

        await using var engine = BrowserAutomationEngine.Create(_browserService, SupportedBrowser.Edge);

        var request = new StructuredExtractionRequest(pageUrl, new[]
        {
            new ExtractionFieldDefinition("name", "#product-name"),
            new ExtractionFieldDefinition("price", ".price"),
            new ExtractionFieldDefinition("buy_url", ".cta-link", "href"),
            new ExtractionFieldDefinition("features", ".features .item", null, IsList: true),
            new ExtractionFieldDefinition("missing", ".non-existent-selector")
        });

        var result = await engine.ExtractAsync(request);

        Assert.True(result.Success);
        Assert.Equal("Product Catalog", result.Title);
        Assert.True(result.IsUntrustedExternalData);
        Assert.Equal(5, result.Fields.Count);

        var nameField = result.Fields.First(f => f.FieldName == "name");
        Assert.Equal("Widget Pro 2000", nameField.Value);
        Assert.True(nameField.Success);

        var priceField = result.Fields.First(f => f.FieldName == "price");
        Assert.Equal("$49.99", priceField.Value);

        var linkField = result.Fields.First(f => f.FieldName == "buy_url");
        Assert.Equal("https://example.com/buy", linkField.Value);

        var featuresField = result.Fields.First(f => f.FieldName == "features");
        Assert.NotNull(featuresField.ListValues);
        Assert.Equal(3, featuresField.ListValues.Count);
        Assert.Equal(new[] { "Fast", "Reliable", "Secure" }, featuresField.ListValues);

        var missingField = result.Fields.First(f => f.FieldName == "missing");
        Assert.Null(missingField.Value);
        Assert.True(missingField.Success);
    }

    [Fact]
    public async Task ExtractAsync_EmptyFields_ThrowsArgumentException()
    {
        var pageUrl = CreateTestHtml("<html><body>Hello</body></html>");
        await using var engine = BrowserAutomationEngine.Create(_browserService, SupportedBrowser.Edge);

        var request = new StructuredExtractionRequest(pageUrl, Array.Empty<ExtractionFieldDefinition>());
        await Assert.ThrowsAsync<ArgumentException>(() => engine.ExtractAsync(request));
    }

    [Fact]
    public async Task BrowserExtractTool_ExecuteAsync_ReturnsValidStructuredJson()
    {
        var pageUrl = CreateTestHtml("<html><head><title>Extract Page</title></head><body><h2 class='title'>Article Title</h2></body></html>");

        await using var engine = BrowserAutomationEngine.Create(_browserService, SupportedBrowser.Edge);
        var tool = new BrowserExtractTool(engine);

        var call = new ToolCall("c_ext_1", "browser_extract", JsonSerializer.Serialize(new
        {
            url = pageUrl,
            fields = new object[]
            {
                new { name = "article_title", selector = ".title" }
            }
        }), DateTimeOffset.UtcNow);

        var result = await tool.ExecuteAsync(call);

        Assert.True(result.IsSuccess);
        using var doc = JsonDocument.Parse(result.OutputJson!);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.True(root.GetProperty("is_untrusted_external_data").GetBoolean());
        var fields = root.GetProperty("fields");
        Assert.Equal(1, fields.GetArrayLength());
        Assert.Equal("Article Title", fields[0].GetProperty("value").GetString());
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
