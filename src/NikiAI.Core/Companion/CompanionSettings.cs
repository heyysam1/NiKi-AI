using NikiAI.Core.Design;

namespace NikiAI.Core.Companion;

/// <summary>
/// Preset sizing options for the companion canvas window.
/// As defined in 01_CORE_CONCEPT.md.
/// </summary>
public enum CompanionSizePreset
{
    Compact,  // 100 x 70
    Standard, // 150 x 100 (Default)
    Large,    // 200 x 135
    Custom
}

/// <summary>
/// Runtime settings and state for the companion desktop shell.
/// </summary>
public class CompanionSettings
{
    public const double MinOpacity = 0.2;
    public const double MaxOpacity = 1.0;

    public CompanionSizePreset SizePreset { get; private set; } = CompanionSizePreset.Standard;
    public double Width { get; private set; } = DesignTokens.CompanionWidthStandard;
    public double Height { get; private set; } = DesignTokens.CompanionHeightStandard;
    public double Opacity { get; private set; } = 1.0;
    public bool AlwaysOnTop { get; set; } = true;
    public bool ReducedMotion { get; set; } = false;
    public double? PositionX { get; set; }
    public double? PositionY { get; set; }

    public void SetSizePreset(CompanionSizePreset preset)
    {
        SizePreset = preset;
        switch (preset)
        {
            case CompanionSizePreset.Compact:
                Width = DesignTokens.CompanionWidthCompact;
                Height = DesignTokens.CompanionHeightCompact;
                break;
            case CompanionSizePreset.Standard:
                Width = DesignTokens.CompanionWidthStandard;
                Height = DesignTokens.CompanionHeightStandard;
                break;
            case CompanionSizePreset.Large:
                Width = DesignTokens.CompanionWidthLarge;
                Height = DesignTokens.CompanionHeightLarge;
                break;
            case CompanionSizePreset.Custom:
                // Retains current width and height
                break;
        }
    }

    public void SetCustomSize(double width, double height)
    {
        if (width < 60 || height < 40)
        {
            throw new ArgumentOutOfRangeException("Dimensions cannot be smaller than 60x40.");
        }
        Width = width;
        Height = height;
        SizePreset = CompanionSizePreset.Custom;
    }

    public void SetOpacity(double opacity)
    {
        Opacity = Math.Clamp(opacity, MinOpacity, MaxOpacity);
    }

    public (double X, double Y) ClampPosition(double targetX, double targetY, double screenLeft, double screenTop, double screenWidth, double screenHeight)
    {
        var minX = screenLeft;
        var maxX = screenLeft + screenWidth - Width;
        var minY = screenTop;
        var maxY = screenTop + screenHeight - Height;

        var clampedX = Math.Clamp(targetX, minX, Math.Max(minX, maxX));
        var clampedY = Math.Clamp(targetY, minY, Math.Max(minY, maxY));

        PositionX = clampedX;
        PositionY = clampedY;
        return (clampedX, clampedY);
    }
}
