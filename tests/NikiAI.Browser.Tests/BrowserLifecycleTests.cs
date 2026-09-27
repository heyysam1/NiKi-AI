using System.Text.Json;
using NikiAI.Browser;
using NikiAI.Core.Browser;
using NikiAI.Core.Security;
using NikiAI.Core.Tools;
using NikiAI.Security;
using NikiAI.Tools;

namespace NikiAI.Browser.Tests;

public class BrowserLifecycleTests
{
    private readonly IBrowserService _browserService = new BrowserService();

    [Fact]
    public async Task BrowserDriver_LaunchAndDispose_CleansUpProcessAndProfile()
    {
        var driver = new CdpBrowserDriver(_browserService, SupportedBrowser.Edge);
        await driver.LaunchAsync();

        Assert.True(driver.IsRunning);

        await driver.DisposeAsync();

        Assert.False(driver.IsRunning);
    }

    [Fact]
    public async Task BrowserDriver_NavigateToBlockedScheme_ThrowsBlockedNavigationException()
    {
        await using var driver = new CdpBrowserDriver(_browserService, SupportedBrowser.Edge);

        await Assert.ThrowsAsync<BlockedNavigationException>(() =>
            driver.NavigateAsync("chrome://downloads", TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task PermissionEngine_ExtractScopeKey_BuildsNormalizedBrowserScopeKeys()
    {
        var permEngine = new PermissionEngine();

        // 1. browser_page_read with URL scopes to host
        var readTool = new BrowserPageReadTool(new BrowserAutomationEngine(new CdpBrowserDriver(_browserService, SupportedBrowser.Edge)));
        var readCall = new ToolCall("c_sc_1", "browser_page_read", """{"url": "https://learn.microsoft.com/en-us/dotnet?query=test"}""", DateTimeOffset.UtcNow);
        var evalRead = await permEngine.EvaluateToolExecutionAsync("task_1", readTool, readCall);
        Assert.True(evalRead.IsAllowed);

        // 2. browser_search scopes to 'web'
        var searchTool = new BrowserSearchTool(new BrowserAutomationEngine(new CdpBrowserDriver(_browserService, SupportedBrowser.Edge)));
        var searchCall = new ToolCall("c_sc_2", "browser_search", """{"query": "unbounded user query here"}""", DateTimeOffset.UtcNow);
        var evalSearch = await permEngine.EvaluateToolExecutionAsync("task_2", searchTool, searchCall);
        Assert.True(evalSearch.IsAllowed);

        // 3. Deny rule on specific domain blocks only that domain
        permEngine.RecordDecision("browser_page_read:read:untrusted-site.com", ApprovalDecision.Deny);
        var deniedCall = new ToolCall("c_sc_3", "browser_page_read", """{"url": "https://untrusted-site.com/malicious"}""", DateTimeOffset.UtcNow);
        var evalDenied = await permEngine.EvaluateToolExecutionAsync("task_3", readTool, deniedCall);
        Assert.False(evalDenied.IsAllowed);
        Assert.Contains("denied by user permission setting", evalDenied.Reason);

        // 4. Other domains remain allowed
        var allowedCall = new ToolCall("c_sc_4", "browser_page_read", """{"url": "https://trusted-site.com/docs"}""", DateTimeOffset.UtcNow);
        var evalAllowed = await permEngine.EvaluateToolExecutionAsync("task_4", readTool, allowedCall);
        Assert.True(evalAllowed.IsAllowed);
    }

    [Fact]
    public async Task BrowserAutomationEngine_BraveBrowserSelection_Supported()
    {
        if (_browserService.IsBrowserAvailable(SupportedBrowser.Brave))
        {
            await using var engine = BrowserAutomationEngine.Create(_browserService, SupportedBrowser.Brave);
            Assert.Equal(SupportedBrowser.Brave, engine.ActiveBrowser);

            var result = await engine.ReadPageAsync("about:blank");
            Assert.NotNull(result);
            Assert.True(result.IsUntrustedExternalData);
        }
    }
}
