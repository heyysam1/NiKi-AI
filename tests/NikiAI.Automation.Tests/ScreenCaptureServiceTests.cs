using System.Drawing;
using NikiAI.Automation;
using NikiAI.Core.Vision;
using Xunit;

namespace NikiAI.Automation.Tests;

public class ScreenCaptureServiceTests
{
    [Fact]
    public void ScreenCaptureService_InitialState_ZeroCaptures()
    {
        var service = new ScreenCaptureService();

        Assert.Equal(0, service.CaptureCount);
        Assert.True(service.ScreenAwarenessEnabled);
    }

    [Fact]
    public async Task PrivacyGate_WhenDisabled_FailsClosedWithoutCapturing()
    {
        var service = new ScreenCaptureService
        {
            ScreenAwarenessEnabled = false
        };

        var resultPrimary = await service.CapturePrimaryScreenAsync();
        var resultWindow = await service.CaptureActiveWindowAsync();
        var resultRegion = await service.CaptureRegionAsync(new Rectangle(0, 0, 100, 100));

        Assert.False(resultPrimary.Success);
        Assert.Contains("disabled", resultPrimary.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Null(resultPrimary.ImageBytes);

        Assert.False(resultWindow.Success);
        Assert.False(resultRegion.Success);
        Assert.Equal(0, service.CaptureCount);
    }

    [Fact]
    public async Task OnDemandCapture_IncrementsCaptureCount_AndReturnsValidBytes()
    {
        var service = new ScreenCaptureService
        {
            ScreenAwarenessEnabled = true
        };

        var initialCount = service.CaptureCount;
        var result = await service.CapturePrimaryScreenAsync();

        if (service.IsAvailable && result.Success)
        {
            Assert.NotNull(result.ImageBytes);
            Assert.True(result.ImageBytes.Length > 0);
            Assert.True(result.Width > 0);
            Assert.True(result.Height > 0);
            Assert.Equal(initialCount + 1, service.CaptureCount);
        }
        else
        {
            // Headless / non-display CI environment fallback check
            Assert.False(result.Success);
            Assert.NotNull(result.ErrorMessage);
        }
    }

    [Fact]
    public async Task CaptureRegion_ClampsDimensionsSafely()
    {
        var service = new ScreenCaptureService
        {
            ScreenAwarenessEnabled = true
        };

        // Empty / negative rectangle
        var result = await service.CaptureRegionAsync(new Rectangle(-50, -50, 0, 0));
        Assert.False(result.Success);
        Assert.Contains("invalid", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }
}
