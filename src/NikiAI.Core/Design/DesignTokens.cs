namespace NikiAI.Core.Design;

/// <summary>
/// Centralized Design System tokens for Niki AI as specified in 03_DESIGN_SYSTEM.md.
/// </summary>
public static class DesignTokens
{
    // Primary Brand Accent
    public const string PrimaryBrand = "#F97316"; // Niki Orange
    public const string BrandAccentHover = "#EA580C";

    // Core Surfaces (Dark Charcoal Foundation)
    public const string Background = "#0F1115";
    public const string Surface1 = "#151922";
    public const string Surface2 = "#1B212B";
    public const string Surface3 = "#222A35";

    // Borders
    public const string Border = "#14FFFFFF";       // rgba(255, 255, 255, 0.08)
    public const string StrongBorder = "#24FFFFFF"; // rgba(255, 255, 255, 0.14)

    // Typography Colors
    public const string TextPrimary = "#F8FAFC";
    public const string TextSecondary = "#B8C0CC";
    public const string TextMuted = "#7E8795";
    public const string TextDisabled = "#626A76";

    // Semantic Status Colors
    public const string StatusSuccess = "#22C55E";
    public const string StatusWarning = "#F59E0B";
    public const string StatusError = "#EF4444";
    public const string StatusInfo = "#60A5FA";

    // Typography
    public const string PrimaryFontFamily = "Inter";
    public const double FontSizeDisplay = 32.0;
    public const double FontSizeH1 = 24.0;
    public const double FontSizeH2 = 18.0;
    public const double FontSizeH3 = 15.0;
    public const double FontSizeBody = 14.0;
    public const double FontSizeSmall = 12.0;
    public const double FontSizeMicro = 11.0;

    // Shape Language & Corner Radii
    public const double CardCornerRadius = 16.0;
    public const double ControlCornerRadius = 11.0;
    public const double PillCornerRadius = 999.0;
    public const double ButtonHeight = 40.0;
    public const double WidgetCornerRadius = 14.0;

    // Desktop Companion Canvas Footprint
    public const double CompanionWidthStandard = 150.0;
    public const double CompanionHeightStandard = 100.0;

    public const double CompanionWidthCompact = 100.0;
    public const double CompanionHeightCompact = 70.0;

    public const double CompanionWidthLarge = 200.0;
    public const double CompanionHeightLarge = 135.0;

    // Motion Durations (Milliseconds)
    public const int MicroInteractionDurationMs = 180;
    public const int PanelTransitionDurationMs = 260;

    // Glassmorphism parameters
    public const double GlassBackdropBlurRadius = 20.0;
    public const double GlassBackgroundOpacity = 0.85;
}
