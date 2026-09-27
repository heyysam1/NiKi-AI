using NikiAI.Core.Companion;
using NikiAI.Core.Design;

namespace NikiAI.Core.Tests;

public class CompanionSettingsTests
{
    [Fact]
    public void Defaults_MatchSpecification()
    {
        var settings = new CompanionSettings();

        Assert.Equal(CompanionSizePreset.Standard, settings.SizePreset);
        Assert.Equal(DesignTokens.CompanionWidthStandard, settings.Width);
        Assert.Equal(DesignTokens.CompanionHeightStandard, settings.Height);
        Assert.Equal(1.0, settings.Opacity);
        Assert.True(settings.AlwaysOnTop);
    }

    [Theory]
    [InlineData(CompanionSizePreset.Compact, 100.0, 70.0)]
    [InlineData(CompanionSizePreset.Standard, 150.0, 100.0)]
    [InlineData(CompanionSizePreset.Large, 200.0, 135.0)]
    public void SetSizePreset_UpdatesDimensionsAccurately(CompanionSizePreset preset, double expectedWidth, double expectedHeight)
    {
        var settings = new CompanionSettings();
        settings.SetSizePreset(preset);

        Assert.Equal(preset, settings.SizePreset);
        Assert.Equal(expectedWidth, settings.Width);
        Assert.Equal(expectedHeight, settings.Height);
    }

    [Theory]
    [InlineData(1.5, 1.0)]       // Exceeds max -> clamped to 1.0
    [InlineData(-0.5, 0.2)]      // Below min -> clamped to 0.2
    [InlineData(0.0, 0.2)]       // Zero -> clamped to 0.2
    [InlineData(0.85, 0.85)]     // Valid mid-range
    [InlineData(0.50, 0.50)]     // Valid mid-range
    public void SetOpacity_ClampsBetweenMinAndMax(double input, double expected)
    {
        var settings = new CompanionSettings();
        settings.SetOpacity(input);

        Assert.Equal(expected, settings.Opacity, precision: 2);
    }

    [Fact]
    public void ClampPosition_ClampsToWorkAreaBoundaries()
    {
        var settings = new CompanionSettings(); // Standard 150x100
        double screenLeft = 0;
        double screenTop = 0;
        double screenWidth = 1920;
        double screenHeight = 1080;

        // Case 1: Target position inside work area -> unchanged
        var (x1, y1) = settings.ClampPosition(500, 300, screenLeft, screenTop, screenWidth, screenHeight);
        Assert.Equal(500, x1);
        Assert.Equal(300, y1);

        // Case 2: Target beyond right/bottom edge -> clamped to (1920 - 150 = 1770, 1080 - 100 = 980)
        var (x2, y2) = settings.ClampPosition(2000, 1200, screenLeft, screenTop, screenWidth, screenHeight);
        Assert.Equal(1770, x2);
        Assert.Equal(980, y2);

        // Case 3: Target negative left/top -> clamped to (0, 0)
        var (x3, y3) = settings.ClampPosition(-100, -50, screenLeft, screenTop, screenWidth, screenHeight);
        Assert.Equal(0, x3);
        Assert.Equal(0, y3);
    }
}
