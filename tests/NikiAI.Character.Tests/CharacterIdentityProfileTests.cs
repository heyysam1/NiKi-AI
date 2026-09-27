using NikiAI.Character;
using NikiAI.Core.Character;
using Xunit;

namespace NikiAI.Character.Tests;

public class CharacterIdentityProfileTests
{
    [Fact]
    public void CharacterRegistry_ProvidesSoleAuthoritativeIdentityRoster()
    {
        var registry = new CharacterRegistry();
        var identities = registry.GetAllIdentityProfiles();

        Assert.NotNull(identities);
        Assert.True(identities.Count >= 8, $"Expected at least 8 identity profiles, found {identities.Count}");

        // Retained characters
        var niki = registry.GetIdentityProfile("niki");
        Assert.NotNull(niki);
        Assert.Equal("Niki", niki.DisplayName);
        Assert.Equal(ExpressionTheme.StandardAnime, niki.ExpressionTheme);
        Assert.True(niki.HasCapability(CharacterCapability.HeadTilt));

        var dog = registry.GetIdentityProfile("dog");
        Assert.NotNull(dog);
        Assert.Equal("Dog", dog.DisplayName);
        Assert.Equal(ExpressionTheme.Energetic, dog.ExpressionTheme);
        Assert.True(dog.HasCapability(CharacterCapability.Tail));
        Assert.True(dog.HasCapability(CharacterCapability.FloppyEars));

        // Dog / Biscuit mapping must be explicit and non-destructive
        var biscuit = registry.GetIdentityProfile("biscuit");
        Assert.NotNull(biscuit);
        Assert.Equal(dog.DisplayName, biscuit.DisplayName);

        var dogChar = registry.GetCharacter("dog");
        var biscuitChar = registry.GetCharacter("biscuit");
        Assert.NotNull(dogChar);
        Assert.NotNull(biscuitChar);
        Assert.True(dogChar.Id == "dog" || dogChar.Id == "biscuit");
        Assert.True(biscuitChar.Id == "biscuit" || biscuitChar.Id == "dog");
    }

    [Theory]
    [InlineData("character-03-astronaut-cat", "Astronaut Cat", ExpressionTheme.CelestialQuiet)]
    [InlineData("character-04-knight", "Knight", ExpressionTheme.Heraldic)]
    [InlineData("character-05-orange-astronaut-cat", "Orange Astronaut Cat", ExpressionTheme.CelestialEnergetic)]
    [InlineData("character-06-goth-girl", "Goth Girl", ExpressionTheme.DarkReserved)]
    [InlineData("character-07-retro-boy", "Retro Boy", ExpressionTheme.CasualRelaxed)]
    [InlineData("character-08-red-cap-adventurer", "Red-Cap Adventurer", ExpressionTheme.Adventurous)]
    public void CharacterRegistry_ContainsApprovedMockupIdentities(string id, string expectedName, ExpressionTheme expectedTheme)
    {
        var registry = new CharacterRegistry();
        var profile = registry.GetIdentityProfile(id);

        Assert.NotNull(profile);
        Assert.Equal(expectedName, profile.DisplayName);
        Assert.Equal(expectedTheme, profile.ExpressionTheme);
        Assert.False(string.IsNullOrWhiteSpace(profile.Species));
        Assert.False(string.IsNullOrWhiteSpace(profile.PersonalityDescription));
        Assert.False(string.IsNullOrWhiteSpace(profile.SignatureDescription));
        Assert.NotEmpty(profile.NaturalEmotionalRange);
    }

    [Fact]
    public void Knight_Anatomy_HasArmorAndHelmet_LacksTail()
    {
        var registry = new CharacterRegistry();
        var knight = registry.GetIdentityProfile("character-04-knight");

        Assert.NotNull(knight);
        Assert.True(knight.HasCapability(CharacterCapability.Helmet));
        Assert.True(knight.HasCapability(CharacterCapability.Armor));
        Assert.False(knight.HasCapability(CharacterCapability.Tail));
    }

    [Fact]
    public void AstronautCat_Anatomy_HasHelmetAndTail()
    {
        var registry = new CharacterRegistry();
        var cat = registry.GetIdentityProfile("character-03-astronaut-cat");

        Assert.NotNull(cat);
        Assert.True(cat.HasCapability(CharacterCapability.Helmet));
        Assert.True(cat.HasCapability(CharacterCapability.Tail));
        Assert.True(cat.HasCapability(CharacterCapability.CatEars));
    }
}
