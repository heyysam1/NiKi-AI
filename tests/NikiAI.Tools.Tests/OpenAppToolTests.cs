using NikiAI.Core.Tools;
using NikiAI.Tools;
using Xunit;

namespace NikiAI.Tools.Tests;

public class OpenAppToolTests
{
    private class MockProcessLauncher : IProcessLauncher
    {
        public bool ShouldSucceed { get; set; } = true;
        public ApprovedAppEntry? LastLaunchedApp { get; private set; }
        public string? LastArguments { get; private set; }

        public Task<ProcessLaunchResult> LaunchApprovedAppAsync(
            ApprovedAppEntry app,
            string? arguments = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastLaunchedApp = app;
            LastArguments = arguments;

            if (ShouldSucceed)
            {
                return Task.FromResult(ProcessLaunchResult.Successful(app.DisplayName, 1234));
            }
            return Task.FromResult(ProcessLaunchResult.Failed(app.DisplayName, "Access denied by OS test harness."));
        }
    }

    private readonly ApprovedAppRegistry _registry = new();
    private readonly MockProcessLauncher _launcher = new();

    [Fact]
    public async Task ExecuteAsync_ApprovedApp_LaunchesSuccessfully()
    {
        var tool = new OpenAppTool(_registry, _launcher);
        var call = new ToolCall("c1", "open_app", """{ "app_name": "notepad" }""", DateTimeOffset.UtcNow);

        var result = await tool.ExecuteAsync(call);

        Assert.True(result.IsSuccess);
        Assert.NotNull(_launcher.LastLaunchedApp);
        Assert.Equal("Notepad", _launcher.LastLaunchedApp.DisplayName);
        Assert.Contains("Launched", result.OutputJson);
    }

    [Theory]
    [InlineData("cmd")]
    [InlineData("cmd.exe")]
    [InlineData("powershell")]
    [InlineData("powershell.exe")]
    [InlineData("pwsh")]
    [InlineData("bash")]
    public async Task ExecuteAsync_CommandShells_StrictlyProhibited(string shellName)
    {
        var tool = new OpenAppTool(_registry, _launcher);
        var call = new ToolCall("c2", "open_app", $$"""{ "app_name": "{{shellName}}" }""", DateTimeOffset.UtcNow);

        var result = await tool.ExecuteAsync(call);

        Assert.False(result.IsSuccess);
        Assert.Contains("restricted command shell", result.ErrorMessage);
        Assert.Null(_launcher.LastLaunchedApp); // Must never touch launcher!
    }

    [Theory]
    [InlineData("chrome")]
    [InlineData("Google Chrome")]
    [InlineData("chrome.exe")]
    public async Task ExecuteAsync_Chrome_NotInControlledAppCatalog(string chromeName)
    {
        var tool = new OpenAppTool(_registry, _launcher);
        var call = new ToolCall("c2b", "open_app", $$"""{ "app_name": "{{chromeName}}" }""", DateTimeOffset.UtcNow);

        var result = await tool.ExecuteAsync(call);

        Assert.False(result.IsSuccess);
        Assert.Contains("not in the controlled approved application catalog", result.ErrorMessage);
        Assert.Null(_launcher.LastLaunchedApp);
    }

    [Fact]
    public async Task ExecuteAsync_ArbitraryExecutable_RejectedWithoutLaunch()
    {
        var tool = new OpenAppTool(_registry, _launcher);
        var call = new ToolCall("c3", "open_app", """{ "app_name": "unknown_app.exe" }""", DateTimeOffset.UtcNow);

        var result = await tool.ExecuteAsync(call);

        Assert.False(result.IsSuccess);
        Assert.Contains("not in the controlled approved application catalog", result.ErrorMessage);
        Assert.Null(_launcher.LastLaunchedApp);
    }

    [Fact]
    public async Task ExecuteAsync_LauncherFailure_ReturnsStructuredFailure()
    {
        _launcher.ShouldSucceed = false;
        var tool = new OpenAppTool(_registry, _launcher);
        var call = new ToolCall("c4", "open_app", """{ "app_name": "calc" }""", DateTimeOffset.UtcNow);

        var result = await tool.ExecuteAsync(call);

        Assert.False(result.IsSuccess);
        Assert.Contains("Failed to launch application", result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_Cancellation_ReturnsCancelledFailure()
    {
        var tool = new OpenAppTool(_registry, _launcher);
        var call = new ToolCall("c5", "open_app", """{ "app_name": "notepad" }""", DateTimeOffset.UtcNow);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await tool.ExecuteAsync(call, cts.Token);

        Assert.False(result.IsSuccess);
        Assert.Contains("cancelled", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }
}
