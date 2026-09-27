using NikiAI.Browser;
using NikiAI.Core.Browser;
using Xunit;

namespace NikiAI.Browser.Tests;

public class BrowserGuardrailTests
{
    [Theory]
    [InlineData("edge", SupportedBrowser.Edge)]
    [InlineData("MSEDGE", SupportedBrowser.Edge)]
    [InlineData("brave", SupportedBrowser.Brave)]
    [InlineData("Brave", SupportedBrowser.Brave)]
    [InlineData("chrome", SupportedBrowser.Chrome)]
    [InlineData("Google Chrome", SupportedBrowser.Chrome)]
    [InlineData("google-chrome", SupportedBrowser.Chrome)]
    [InlineData("googlechrome", SupportedBrowser.Chrome)]
    [InlineData("custom", SupportedBrowser.Custom)]
    public void BrowserGuardrail_ParseAndValidate_ValidatesSupportedBrowsers(string input, SupportedBrowser expected)
    {
        var result = BrowserGuardrail.ParseAndValidate(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("netscape")]
    [InlineData("safari_unsupported")]
    [InlineData("invalid_browser_xyz")]
    public void BrowserGuardrail_ParseAndValidate_RejectsUnsupported(string input)
    {
        Assert.Throws<ArgumentException>(() => BrowserGuardrail.ParseAndValidate(input));
    }

    [Fact]
    public void BrowserService_EdgeBraveAndChrome_AreSupportedConfigurations()
    {
        var service = new BrowserService();

        // Edge is supported
        service.SetBrowser(SupportedBrowser.Edge);
        Assert.Equal(SupportedBrowser.Edge, service.CurrentBrowser);

        // Brave is supported
        service.SetBrowser(SupportedBrowser.Brave);
        Assert.Equal(SupportedBrowser.Brave, service.CurrentBrowser);

        // Chrome is supported
        service.SetBrowser(SupportedBrowser.Chrome);
        Assert.Equal(SupportedBrowser.Chrome, service.CurrentBrowser);

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
    }

    [Fact]
    public void BrowserService_ValidateRequestedBrowser_AcceptsKnownBrowsersWithoutError()
    {
        // Chrome, Edge, and Brave should validate cleanly without error
        BrowserService.ValidateRequestedBrowser("Google Chrome");
        BrowserService.ValidateRequestedBrowser("chrome");
        BrowserService.ValidateRequestedBrowser("edge");
        BrowserService.ValidateRequestedBrowser("brave");
    }

    [Fact]
    public void BrowserService_ValidateRequestedBrowser_RejectsUnknownBrowsers()
    {
        Assert.Throws<ArgumentException>(() =>
            BrowserService.ValidateRequestedBrowser("unknown_browser_123"));
    }

    [Fact]
    public async Task BrowserAdapterRegistry_ResolvesChromiumAdapter_ForChromiumDescriptor()
    {
        var registry = new BrowserAdapterRegistry();
        var adapter = new ChromiumCdpAdapter();
        registry.RegisterAdapter(adapter);

        var tempExe = Path.GetTempFileName();
        try
        {
            var descriptor = new BrowserDescriptor("test-chromium", "Test Chromium", tempExe, BrowserFamily.Chromium, "chromium-cdp");
            var resolved = await registry.ResolveAdapterAsync(descriptor);

            Assert.NotNull(resolved);
            Assert.Equal("chromium-cdp", resolved!.AdapterKey);
        }
        finally
        {
            if (File.Exists(tempExe))
            {
                File.Delete(tempExe);
            }
        }
    }

    [Fact]
    public async Task BrowserAdapterRegistry_RejectsNonChromium_ForChromiumOnlyAdapter()
    {
        var registry = new BrowserAdapterRegistry();
        registry.RegisterAdapter(new ChromiumCdpAdapter());

        var tempExe = Path.GetTempFileName();
        try
        {
            var descriptor = new BrowserDescriptor("test-gecko", "Test Gecko", tempExe, BrowserFamily.Gecko);
            var resolved = await registry.ResolveAdapterAsync(descriptor);

            Assert.Null(resolved);
        }
        finally
        {
            if (File.Exists(tempExe))
            {
                File.Delete(tempExe);
            }
        }
    }
}
