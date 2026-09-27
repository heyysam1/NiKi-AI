using NikiAI.Core.Design;

namespace NikiAI.Core.Tests;

public class DesignTokensTests
{
    [Fact]
    public void PrimaryBrandColor_MustMatchApprovedNikiOrange()
    {
        Assert.Equal("#F97316", DesignTokens.PrimaryBrand);
    }

    [Fact]
    public void CoreSurfaces_MustMatchApprovedCharcoalPalette()
    {
        Assert.Equal("#0F1115", DesignTokens.Background);
        Assert.Equal("#151922", DesignTokens.Surface1);
        Assert.Equal("#1B212B", DesignTokens.Surface2);
        Assert.Equal("#222A35", DesignTokens.Surface3);
    }

    [Fact]
    public void CompanionCanvasStandardFootprint_MustBe150x100()
    {
        Assert.Equal(150.0, DesignTokens.CompanionWidthStandard);
        Assert.Equal(100.0, DesignTokens.CompanionHeightStandard);
    }

    [Fact]
    public void SemanticStatusColors_MustMatchSpecification()
    {
        Assert.Equal("#22C55E", DesignTokens.StatusSuccess);
        Assert.Equal("#F59E0B", DesignTokens.StatusWarning);
        Assert.Equal("#EF4444", DesignTokens.StatusError);
        Assert.Equal("#60A5FA", DesignTokens.StatusInfo);
    }

    [Fact]
    public void ShapeLanguage_CornerRadiiMustBeConsistent()
    {
        Assert.Equal(16.0, DesignTokens.CardCornerRadius);
        Assert.Equal(11.0, DesignTokens.ControlCornerRadius);
        Assert.Equal(999.0, DesignTokens.PillCornerRadius);
    }
}
