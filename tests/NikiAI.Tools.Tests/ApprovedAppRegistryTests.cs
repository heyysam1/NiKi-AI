using NikiAI.Tools;
using Xunit;

namespace NikiAI.Tools.Tests;

public class ApprovedAppRegistryTests
{
    private readonly ApprovedAppRegistry _registry = new();

    [Theory]
    [InlineData("notepad", "Notepad", "notepad.exe")]
    [InlineData("calc", "Calculator", "calc.exe")]
    [InlineData("calculator", "Calculator", "calc.exe")]
    [InlineData("paint", "Paint", "mspaint.exe")]
    [InlineData("mspaint", "Paint", "mspaint.exe")]
    [InlineData("code", "Visual Studio Code", "code")]
    [InlineData("vscode", "Visual Studio Code", "code")]
    [InlineData("edge", "Microsoft Edge", "msedge.exe")]
    [InlineData("msedge", "Microsoft Edge", "msedge.exe")]
    [InlineData("brave", "Brave Browser", "brave.exe")]
    [InlineData("explorer", "File Explorer", "explorer.exe")]
    [InlineData("terminal", "Windows Terminal", "wt.exe")]
    [InlineData("taskmgr", "Task Manager", "taskmgr.exe")]
    public void TryResolveApp_ApprovedAppsAndAliases_ResolveCorrectly(string query, string expectedName, string expectedExe)
    {
        var resolved = _registry.TryResolveApp(query, out var entry);

        Assert.True(resolved);
        Assert.NotNull(entry);
        Assert.Equal(expectedName, entry.DisplayName);
        Assert.Equal(expectedExe, entry.ExecutableName);
    }

    [Theory]
    [InlineData("cmd")]
    [InlineData("cmd.exe")]
    [InlineData("powershell")]
    [InlineData("powershell.exe")]
    [InlineData("pwsh")]
    [InlineData("bash")]
    public void IsProhibitedApp_CommandShells_ReturnsTrueWithReason(string shellTarget)
    {
        var prohibited = _registry.IsProhibitedApp(shellTarget, out var reason);

        Assert.True(prohibited);
        Assert.NotNull(reason);
        Assert.Contains("restricted command shell", reason);
    }

    [Theory]
    [InlineData("chrome")]
    [InlineData("Google Chrome")]
    [InlineData("chrome.exe")]
    public void IsProhibitedApp_Chrome_ReturnsFalse(string chromeTarget)
    {
        var prohibited = _registry.IsProhibitedApp(chromeTarget, out var reason);

        Assert.False(prohibited);
        Assert.Null(reason);
    }

    [Theory]
    [InlineData("chrome")]
    [InlineData("google-chrome")]
    [InlineData("chrome.exe")]
    public void TryResolveApp_Chrome_FailsResolution(string chromeTarget)
    {
        var resolved = _registry.TryResolveApp(chromeTarget, out var entry);

        Assert.False(resolved);
        Assert.Null(entry);
    }

    [Theory]
    [InlineData("cmd.exe")]
    [InlineData("powershell.exe")]
    [InlineData("C:\\Windows\\System32\\cmd.exe")]
    [InlineData("..\\..\\script.bat")]
    [InlineData("/bin/sh")]
    [InlineData("notepad & calc")]
    [InlineData("calc | dir")]
    [InlineData("unknown_app_xyz")]
    public void TryResolveApp_ArbitraryPathsOrUnapprovedApps_Rejected(string unapprovedInput)
    {
        var resolved = _registry.TryResolveApp(unapprovedInput, out var entry);

        Assert.False(resolved);
        Assert.Null(entry);
    }

    [Fact]
    public void GetApprovedApps_ReturnsListOfAllowedApps()
    {
        var list = _registry.GetApprovedApps();

        Assert.NotEmpty(list);
        Assert.Contains(list, a => a.Key == "notepad");
        Assert.Contains(list, a => a.Key == "edge");
        Assert.Contains(list, a => a.Key == "brave");
        Assert.DoesNotContain(list, a => a.Key.Contains("chrome", StringComparison.OrdinalIgnoreCase));
    }
}
