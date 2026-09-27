using NikiAI.Core.Tools;
using NikiAI.Tools;
using Xunit;

namespace NikiAI.Tools.Tests;

public class ClipboardToolsTests
{
    private class MockClipboardService : IClipboardService
    {
        private string? _currentText;

        public Task<string?> GetTextAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_currentText);
        }

        public Task SetTextAsync(string text, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _currentText = text;
            return Task.CompletedTask;
        }
    }

    private readonly MockClipboardService _clipboardService = new();

    [Fact]
    public async Task ReadClipboardTool_WhenEmpty_ReturnsNoText()
    {
        var tool = new ReadClipboardTool(_clipboardService);
        var call = new ToolCall("cb1", "read_clipboard", "{}", DateTimeOffset.UtcNow);

        var result = await tool.ExecuteAsync(call);

        Assert.True(result.IsSuccess);
        Assert.Contains("\"has_text\":false", result.OutputJson);
        Assert.Contains("\"character_count\":0", result.OutputJson);
    }

    [Fact]
    public async Task ReadClipboardTool_WhenPopulated_ReturnsContentLocally()
    {
        await _clipboardService.SetTextAsync("Secret local data");
        var tool = new ReadClipboardTool(_clipboardService);
        var call = new ToolCall("cb2", "read_clipboard", "{}", DateTimeOffset.UtcNow);

        var result = await tool.ExecuteAsync(call);

        Assert.True(result.IsSuccess);
        Assert.Contains("\"has_text\":true", result.OutputJson);
        Assert.Contains("Secret local data", result.OutputJson);
    }

    [Fact]
    public async Task WriteClipboardTool_ValidText_SetsClipboardContent()
    {
        var tool = new WriteClipboardTool(_clipboardService);
        var call = new ToolCall("cb3", "write_clipboard", """{ "text": "Hello Niki AI" }""", DateTimeOffset.UtcNow);

        var result = await tool.ExecuteAsync(call);

        Assert.True(result.IsSuccess);
        var currentText = await _clipboardService.GetTextAsync();
        Assert.Equal("Hello Niki AI", currentText);
        Assert.Contains("\"character_count\":13", result.OutputJson);
    }

    [Fact]
    public async Task ClipboardTools_Cancellation_AbortsCleanly()
    {
        var readTool = new ReadClipboardTool(_clipboardService);
        var call = new ToolCall("cb4", "read_clipboard", "{}", DateTimeOffset.UtcNow);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await readTool.ExecuteAsync(call, cts.Token);

        Assert.False(result.IsSuccess);
        Assert.Contains("cancelled", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }
}
