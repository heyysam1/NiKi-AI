using NikiAI.Browser;
using NikiAI.Core.Browser;

namespace NikiAI.Browser.Tests;

public class BrowserGuardrailTests
{
    [Theory]
    [InlineData("chrome")]
    [InlineData("CHROME")]
    [InlineData("google chrome")]
    [InlineData("chrome.exe")]
    [InlineData(@"C:\Program Files\Google\Chrome\Application\chrome.exe")]
    public void BrowserGuardrail_AssertNotChrome_ThrowsChromeProhibitedException(string prohibitedIdentifier)
    {
        var ex = Assert.Throws<ChromeProhibitedException>(() =>
            BrowserGuardrail.AssertNotChrome(prohibitedIdentifier));

        Assert.Contains("Google Chrome is strictly prohibited", ex.Message);
    }

    [Theory]
    [InlineData("edge", SupportedBrowser.Edge)]
    [InlineData("MSEDGE", SupportedBrowser.Edge)]
    [InlineData("brave", SupportedBrowser.Brave)]
    [InlineData("Brave", SupportedBrowser.Brave)]
    public void BrowserGuardrail_ParseAndValidate_ValidatesSupportedBrowsers(string input, SupportedBrowser expected)
    {
        var result = BrowserGuardrail.ParseAndValidate(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("chrome")]
    [InlineData("Google Chrome")]
    public void BrowserGuardrail_ParseAndValidate_RejectsChrome(string input)
    {
        Assert.Throws<ChromeProhibitedException>(() => BrowserGuardrail.ParseAndValidate(input));
    }

    [Fact]
    public void BrowserService_EdgeAndBrave_AreSupportedAndDiscovered()
    {
        var service = new BrowserService();

        // Edge is supported
        service.SetBrowser(SupportedBrowser.Edge);
        Assert.Equal(SupportedBrowser.Edge, service.CurrentBrowser);

        // Brave is supported
        service.SetBrowser(SupportedBrowser.Brave);
        Assert.Equal(SupportedBrowser.Brave, service.CurrentBrowser);

        // On this Windows machine, Edge is installed
        Assert.True(service.IsBrowserAvailable(SupportedBrowser.Edge));
    }

    [Fact]
    public async Task BrowserService_GetBrowserExecutablePath_ReturnsValidPathForInstalledBrowser()
    {
        var service = new BrowserService();
        var edgePath = await service.GetBrowserExecutablePathAsync(SupportedBrowser.Edge);

        Assert.NotNull(edgePath);
        Assert.True(File.Exists(edgePath));
        Assert.Contains("msedge.exe", edgePath, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("chrome.exe", edgePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BrowserService_ValidateRequestedBrowser_RejectsChromeExplicitly()
    {
        Assert.Throws<ChromeProhibitedException>(() =>
            BrowserService.ValidateRequestedBrowser("Google Chrome"));
    }
}
