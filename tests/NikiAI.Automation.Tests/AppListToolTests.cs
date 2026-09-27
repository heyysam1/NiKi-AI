using System.Text.Json;
using NikiAI.Automation;
using NikiAI.Core.Automation;
using NikiAI.Core.Tools;
using NikiAI.Tools;

namespace NikiAI.Automation.Tests;

public class AppListToolTests : IClassFixture<TestWindowFixture>
{
    private readonly IWindowsAutomationService _automationService = new WindowsAutomationService();
    private readonly TestWindowFixture _fixture;

    public AppListToolTests(TestWindowFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetRunningAppsAsync_ReturnsValidApplications()
    {
        var apps = await _automationService.GetRunningAppsAsync();
        Assert.NotNull(apps);
        Assert.NotEmpty(apps);

        var first = apps[0];
        Assert.True(first.ProcessId > 0);
        Assert.False(string.IsNullOrWhiteSpace(first.ProcessName));
        Assert.False(string.IsNullOrWhiteSpace(first.WindowTitle));
        Assert.NotEqual(nint.Zero, first.MainWindowHandle);
    }

    [Fact]
    public async Task GetRecentAppsAsync_RespectsLimit()
    {
        var recent = await _automationService.GetRecentAppsAsync(3);
        Assert.NotNull(recent);
        Assert.InRange(recent.Count, 1, 3);
    }

    [Fact]
    public async Task AppListTool_ExecuteAsync_ReturnsStructuredJson()
    {
        var tool = new AppListTool(_automationService);
        var call = new ToolCall("c_app_1", "app_list", "{}", DateTimeOffset.UtcNow);

        var result = await tool.ExecuteAsync(call);
        Assert.True(result.IsSuccess);

        using var doc = JsonDocument.Parse(result.OutputJson!);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.True(root.GetProperty("count").GetInt32() > 0);
        Assert.NotEmpty(root.GetProperty("apps").EnumerateArray());
    }

    [Fact]
    public async Task RecentAppsTool_ExecuteAsync_ReturnsStructuredJson()
    {
        var tool = new RecentAppsTool(_automationService);
        var call = new ToolCall("c_rec_1", "recent_apps", JsonSerializer.Serialize(new { limit = 2 }), DateTimeOffset.UtcNow);

        var result = await tool.ExecuteAsync(call);
        Assert.True(result.IsSuccess);

        using var doc = JsonDocument.Parse(result.OutputJson!);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.InRange(root.GetProperty("count").GetInt32(), 1, 2);
    }
}
