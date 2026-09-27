using System;
using System.Collections.Generic;
using System.IO;
using NikiAI.Character;
using NikiAI.Core.Character;
using Xunit;

namespace NikiAI.Character.Tests;

public class CharacterRegistryTests
{
    [Fact]
    public void CharacterRegistry_InitializesWithDefaultNiki()
    {
        var registry = new CharacterRegistry();

        Assert.NotNull(registry.ActiveCharacter);
        Assert.Equal("niki", registry.ActiveCharacter.Id, ignoreCase: true);
    }

    [Fact]
    public void CharacterRegistry_ContainsRequiredRoster_NikiMochiBiscuit()
    {
        var registry = new CharacterRegistry();
        var all = registry.GetAllCharacters();

        Assert.True(all.Count >= 3);
        Assert.Contains(all, c => c.Id.Equals("niki", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(all, c => c.Id.Equals("mochi", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(all, c => c.Id.Equals("biscuit", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CharacterRegistry_CanSwitchCharacters_AndFiresEvent()
    {
        var registry = new CharacterRegistry();
        CharacterProfile? updated = null;
        var fired = false;

        registry.ActiveCharacterChanged += (s, profile) =>
        {
            fired = true;
            updated = profile;
        };

        registry.SetActiveCharacter("mochi");

        Assert.True(fired);
        Assert.NotNull(updated);
        Assert.Equal("mochi", updated.Id);
        Assert.Equal("mochi", registry.ActiveCharacter.Id);

        // Switch to Biscuit
        fired = false;
        registry.SetActiveCharacter("biscuit");
        Assert.True(fired);
        Assert.Equal("biscuit", registry.ActiveCharacter.Id);
    }

    [Fact]
    public void CharacterRegistry_SettingUnknownCharacter_ThrowsKeyNotFoundException()
    {
        var registry = new CharacterRegistry();
        var currentId = registry.ActiveCharacter.Id;

        Assert.Throws<KeyNotFoundException>(() => registry.SetActiveCharacter("non_existent_character_id"));
        Assert.Equal(currentId, registry.ActiveCharacter.Id);
    }

    [Fact]
    public void CharacterRegistry_SettingNullOrEmpty_ThrowsArgumentException()
    {
        var registry = new CharacterRegistry();

        Assert.Throws<ArgumentException>(() => registry.SetActiveCharacter(""));
        Assert.Throws<ArgumentException>(() => registry.SetActiveCharacter(null!));
    }

    [Fact]
    public void CharacterRegistry_LoadsRealManifestsFromDisk_WhenCustomPathProvided()
    {
        // Find assets/characters in project root
        var currentDir = Directory.GetCurrentDirectory();
        while (currentDir != null && !File.Exists(Path.Combine(currentDir, "NikiAI.sln")))
        {
            currentDir = Directory.GetParent(currentDir)?.FullName;
        }

        Assert.NotNull(currentDir);
        var assetsPath = Path.Combine(currentDir, "assets", "characters");

        if (Directory.Exists(assetsPath))
        {
            var registry = new CharacterRegistry(assetsPath);
            var niki = registry.GetCharacter("niki");
            Assert.NotNull(niki);
            Assert.Equal("Niki", niki.DisplayName);
            Assert.True(niki.Animations.ContainsKey(CharacterState.Idle));
            Assert.True(niki.Animations.ContainsKey(CharacterState.Listening));
            Assert.True(niki.Animations.ContainsKey(CharacterState.Thinking));
            Assert.True(niki.Animations.ContainsKey(CharacterState.Working));
            Assert.True(niki.Animations.ContainsKey(CharacterState.Happy));
            Assert.True(niki.Animations.ContainsKey(CharacterState.Notification));
            Assert.True(niki.Animations.ContainsKey(CharacterState.Sleep));
        }
    }

    [Fact]
    public void CharacterRegistry_IsAssetBacked_ValidatesRealFramesOnDisk()
    {
        var registry = new CharacterRegistry();

        // Niki and dog/biscuit have actual assets on disk
        Assert.True(registry.IsAssetBacked("niki"));
        Assert.True(registry.IsAssetBacked("dog"));
        Assert.True(registry.IsAssetBacked("biscuit"));

        // Candidate identities have no sprite frames on disk and are not asset-backed
        Assert.False(registry.IsAssetBacked("character-03-astronaut-cat"));
        Assert.False(registry.IsAssetBacked("character-04-knight"));
        Assert.False(registry.IsAssetBacked("character-05-orange-astronaut-cat"));
        Assert.False(registry.IsAssetBacked("character-06-goth-girl"));
        Assert.False(registry.IsAssetBacked("character-07-retro-boy"));
        Assert.False(registry.IsAssetBacked("character-08-red-cap-adventurer"));

        // Attempting to select a non-asset-backed character throws InvalidOperationException
        Assert.Throws<InvalidOperationException>(() => registry.SetActiveCharacter("character-03-astronaut-cat"));
    }

    [Fact]
    public void CharacterRegistry_ReloadCharacters_PreservesAllEightIdentities()
    {
        var registry = new CharacterRegistry();
        Assert.Equal(8, registry.GetAllIdentityProfiles().Count);

        var loadedCount = registry.ReloadCharacters();
        Assert.True(loadedCount > 0);

        // Crucial requirement: reload must never clear or lose any of the 8 authoritative identities
        var identitiesAfterReload = registry.GetAllIdentityProfiles();
        Assert.Equal(8, identitiesAfterReload.Count);

        Assert.Contains(identitiesAfterReload, p => p.CharacterId == "niki");
        Assert.Contains(identitiesAfterReload, p => p.CharacterId == "dog");
        Assert.Contains(identitiesAfterReload, p => p.CharacterId == "character-03-astronaut-cat");
        Assert.Contains(identitiesAfterReload, p => p.CharacterId == "character-04-knight");
        Assert.Contains(identitiesAfterReload, p => p.CharacterId == "character-05-orange-astronaut-cat");
        Assert.Contains(identitiesAfterReload, p => p.CharacterId == "character-06-goth-girl");
        Assert.Contains(identitiesAfterReload, p => p.CharacterId == "character-07-retro-boy");
        Assert.Contains(identitiesAfterReload, p => p.CharacterId == "character-08-red-cap-adventurer");
    }

    [Fact]
    public void CharacterRegistry_IsAssetBacked_RejectsCorruptOrNonImageFiles()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "NikiAI_AssetBacked_Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var charDir = Path.Combine(tempDir, "dummy-char");
            Directory.CreateDirectory(charDir);

            // Write a dummy non-image file with non-zero bytes (plain text)
            var dummyFrame = Path.Combine(charDir, "frame_0.png");
            File.WriteAllText(dummyFrame, "This is not a real image file, just plain text with non-zero size.");

            // Write character.json pointing to the non-image file
            var manifestJson = """
            {
              "id": "dummy-char",
              "displayName": "Dummy Character",
              "version": "1.0",
              "animations": {
                "Idle": {
                  "state": "Idle",
                  "frameDurationMs": 200,
                  "loop": true,
                  "frames": [
                    { "frameIndex": 0, "imagePath": "frame_0.png" }
                  ]
                }
              }
            }
            """;
            File.WriteAllText(Path.Combine(charDir, "character.json"), manifestJson);

            var registry = new CharacterRegistry(tempDir);
            // The file exists and has size > 0, but is not a valid decodable image
            Assert.False(registry.IsAssetBacked("dummy-char"));
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }
}
