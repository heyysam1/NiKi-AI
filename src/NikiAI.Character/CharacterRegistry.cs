using System.IO;
using System.Text.Json;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using NikiAI.Core.Character;

namespace NikiAI.Character;

/// <summary>
/// Data-driven registry managing character profiles, animations, and anatomical capabilities.
/// Acts as the sole authoritative runtime character registry in Niki AI.
/// Reconciles Spec 11 roster (retained Niki, retained Dog, and 6 approved mockup characters).
/// </summary>
public class CharacterRegistry : ICharacterRegistry
{
    private readonly ILogger<CharacterRegistry>? _logger;
    private readonly Dictionary<string, CharacterProfile> _characters = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CharacterIdentityProfile> _identities = new(StringComparer.OrdinalIgnoreCase);
    private CharacterIdentityProfile? _legacyMochiIdentity;
    private string? _discoveredAssetsDirectory;
    private CharacterProfile _activeCharacter;
    private CharacterIdentityProfile _activeIdentityProfile;

    public event EventHandler<CharacterProfile>? ActiveCharacterChanged;

    public CharacterProfile ActiveCharacter => _activeCharacter;
    public CharacterIdentityProfile ActiveIdentityProfile => _activeIdentityProfile;

    public CharacterRegistry(string? customAssetsPath = null, ILogger<CharacterRegistry>? logger = null)
    {
        _logger = logger;
        LoadCharacters(customAssetsPath);
        RegisterAuthoritativeIdentities();

        // Set default active character to Niki
        if (_characters.TryGetValue("niki", out var niki))
        {
            _activeCharacter = niki;
        }
        else if (_characters.Count > 0)
        {
            _activeCharacter = _characters.Values.First();
        }
        else
        {
            _activeCharacter = CreateFallbackProfile("niki", "Niki", "Human Anime Companion", "Primary AI operator.");
            _characters["niki"] = _activeCharacter;
        }

        _activeIdentityProfile = GetIdentityProfile(_activeCharacter.Id) ?? CreateDefaultIdentityFor(_activeCharacter);
    }

    public IReadOnlyList<CharacterProfile> GetAllCharacters() => _characters.Values.ToList().AsReadOnly();

    public CharacterProfile? GetCharacter(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;

        // Support 'dog' resolving to 'dog' or existing 'biscuit' mapping
        if (id.Equals("dog", StringComparison.OrdinalIgnoreCase) && !_characters.ContainsKey("dog") && _characters.ContainsKey("biscuit"))
        {
            return _characters["biscuit"];
        }

        return _characters.TryGetValue(id, out var profile) ? profile : null;
    }

    public CharacterIdentityProfile? GetIdentityProfile(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;

        // Support 'biscuit' resolving to 'dog' identity or vice-versa
        if (id.Equals("biscuit", StringComparison.OrdinalIgnoreCase) && _identities.TryGetValue("dog", out var dogProfile))
        {
            return dogProfile;
        }

        // Support legacy 'mochi' without polluting the 8 authoritative identities
        if (id.Equals("mochi", StringComparison.OrdinalIgnoreCase) && _legacyMochiIdentity != null)
        {
            return _legacyMochiIdentity;
        }

        return _identities.TryGetValue(id, out var profile) ? profile : null;
    }

    public IReadOnlyList<CharacterIdentityProfile> GetAllIdentityProfiles() => _identities.Values.ToList().AsReadOnly();

    /// <summary>
    /// Checks whether the specified character is backed by valid, loadable on-disk animation assets.
    /// Requires that baseline idle animation frames exist, have length > 0, and are loadable.
    /// </summary>
    public bool IsAssetBacked(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;

        var character = GetCharacter(id);
        if (character == null) return false;

        // Check if baseline idle animation is present
        if (!character.Animations.TryGetValue(CharacterState.Idle, out var idleAnim) || idleAnim.Frames.Count == 0)
        {
            return false;
        }

        // Verify that all referenced frame files in all animations actually exist on disk and are non-empty
        foreach (var anim in character.Animations.Values)
        {
            if (anim.Frames.Count == 0) return false;

            foreach (var frame in anim.Frames)
            {
                if (string.IsNullOrWhiteSpace(frame.ImagePath)) return false;

                string resolvedPath = frame.ImagePath;
                if (!File.Exists(resolvedPath))
                {
                    resolvedPath = Path.Combine(AppContext.BaseDirectory, frame.ImagePath);
                }
                if (!File.Exists(resolvedPath))
                {
                    resolvedPath = Path.Combine(Directory.GetCurrentDirectory(), frame.ImagePath);
                }
                if (!File.Exists(resolvedPath) && _discoveredAssetsDirectory != null)
                {
                    resolvedPath = Path.Combine(Directory.GetParent(_discoveredAssetsDirectory)?.FullName ?? "", frame.ImagePath);
                }
                if (!File.Exists(resolvedPath) && _discoveredAssetsDirectory != null)
                {
                    resolvedPath = Path.Combine(_discoveredAssetsDirectory, Path.GetFileName(Path.GetDirectoryName(frame.ImagePath) ?? ""), Path.GetFileName(frame.ImagePath));
                }
                if (!File.Exists(resolvedPath))
                {
                    var baseDir = new DirectoryInfo(AppContext.BaseDirectory);
                    for (int i = 0; i < 6 && baseDir != null; i++)
                    {
                        var candidate = Path.Combine(baseDir.FullName, frame.ImagePath);
                        if (File.Exists(candidate))
                        {
                            resolvedPath = candidate;
                            break;
                        }
                        baseDir = baseDir.Parent;
                    }
                }
                if (!File.Exists(resolvedPath))
                {
                    var curDir = new DirectoryInfo(Directory.GetCurrentDirectory());
                    for (int i = 0; i < 6 && curDir != null; i++)
                    {
                        var candidate = Path.Combine(curDir.FullName, frame.ImagePath);
                        if (File.Exists(candidate))
                        {
                            resolvedPath = candidate;
                            break;
                        }
                        curDir = curDir.Parent;
                    }
                }

                if (!File.Exists(resolvedPath))
                {
                    return false;
                }

                try
                {
                    var fileInfo = new FileInfo(resolvedPath);
                    if (fileInfo.Length == 0)
                    {
                        return false;
                    }

                    // Validate actual frame loadability and decodability via WPF BitmapDecoder
                    using var stream = File.OpenRead(resolvedPath);
                    var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.None);
                    if (decoder.Frames.Count == 0 || decoder.Frames[0].PixelWidth <= 0 || decoder.Frames[0].PixelHeight <= 0)
                    {
                        return false;
                    }
                }
                catch
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Reloads character animation manifests from disk without losing or redefining authoritative identity profiles.
    /// Preserves all 8 authoritative identities in the registry.
    /// </summary>
    public int ReloadCharacters(string? customAssetsPath = null)
    {
        _logger?.LogInformation("Reloading character animation manifests from disk...");
        _characters.Clear();
        LoadCharacters(customAssetsPath);

        // Maintain active character integrity
        if (_characters.TryGetValue(_activeCharacter.Id, out var reloadedActive))
        {
            _activeCharacter = reloadedActive;
        }
        else if (_characters.TryGetValue("niki", out var niki))
        {
            _activeCharacter = niki;
        }
        else if (_characters.Count > 0)
        {
            _activeCharacter = _characters.Values.First();
        }

        _activeIdentityProfile = GetIdentityProfile(_activeCharacter.Id) ?? CreateDefaultIdentityFor(_activeCharacter);

        _logger?.LogInformation("Character reload complete. Total registered animation profiles: {Count}", _characters.Count);
        return _characters.Count;
    }

    public void SetActiveCharacter(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Character ID cannot be null or empty.", nameof(id));
        }

        var profile = GetCharacter(id);
        if (profile == null)
        {
            throw new KeyNotFoundException($"Character profile with ID '{id}' was not found in registry.");
        }

        if (!IsAssetBacked(profile.Id))
        {
            throw new InvalidOperationException($"Character '{id}' has missing or broken animation frames and is not asset-backed. It cannot be set as active character.");
        }

        if (_activeCharacter.Id != profile.Id)
        {
            _activeCharacter = profile;
            _activeIdentityProfile = GetIdentityProfile(profile.Id) ?? CreateDefaultIdentityFor(profile);
            _logger?.LogInformation("Active character changed to: {Name} ({Id})", profile.DisplayName, profile.Id);
            ActiveCharacterChanged?.Invoke(this, profile);
        }
    }

    private void LoadCharacters(string? customAssetsPath)
    {
        var searchPaths = new List<string>();

        if (!string.IsNullOrWhiteSpace(customAssetsPath) && Directory.Exists(customAssetsPath))
        {
            searchPaths.Add(customAssetsPath);
        }

        searchPaths.Add(Path.Combine(AppContext.BaseDirectory, "Assets", "Characters"));
        searchPaths.Add(Path.Combine(AppContext.BaseDirectory, "assets", "characters"));
        searchPaths.Add(Path.Combine(Directory.GetCurrentDirectory(), "assets", "characters"));
        searchPaths.Add(Path.Combine(Directory.GetCurrentDirectory(), "src", "NikiAI.App", "Assets", "Characters"));

        // Walk up directory hierarchy to locate repo-root assets/characters
        var baseDir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 6 && baseDir != null; i++)
        {
            var candidate = Path.Combine(baseDir.FullName, "assets", "characters");
            if (Directory.Exists(candidate))
            {
                searchPaths.Add(candidate);
                break;
            }
            baseDir = baseDir.Parent;
        }

        var curDir = new DirectoryInfo(Directory.GetCurrentDirectory());
        for (int i = 0; i < 6 && curDir != null; i++)
        {
            var candidate = Path.Combine(curDir.FullName, "assets", "characters");
            if (Directory.Exists(candidate))
            {
                searchPaths.Add(candidate);
                break;
            }
            curDir = curDir.Parent;
        }

        string? validDirectory = null;
        foreach (var path in searchPaths)
        {
            if (Directory.Exists(path))
            {
                validDirectory = path;
                _discoveredAssetsDirectory = path;
                break;
            }
        }

        if (validDirectory != null)
        {
            _logger?.LogInformation("Discovering character profiles in: {Directory}", validDirectory);
            var subDirs = Directory.GetDirectories(validDirectory);
            foreach (var charDir in subDirs)
            {
                var manifestPath = Path.Combine(charDir, "character.json");
                if (File.Exists(manifestPath))
                {
                    try
                    {
                        var profile = LoadProfileFromManifest(charDir, manifestPath);
                        if (profile != null)
                        {
                            _characters[profile.Id] = profile;
                            _logger?.LogInformation("Registered character profile: {Name} ({Id}) with {Count} animations", profile.DisplayName, profile.Id, profile.Animations.Count);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Failed to load character manifest from {Path}", manifestPath);
                    }
                }
            }
        }

        // Ensure required retained characters exist with fallback if disk load was partial
        EnsureRequiredCharacter("niki", "Niki", "Human Anime Companion", "Primary AI operator. Calm, focused, helpful, and concise.");

        // Dog / Biscuit mapping: reconcile existing disk 'biscuit' with authoritative 'dog'
        if (_characters.TryGetValue("biscuit", out var biscuitProfile))
        {
            // Map 'dog' to biscuit's animations
            _characters["dog"] = new CharacterProfile("dog", "Dog", "Dog Companion", biscuitProfile.Description, new Dictionary<CharacterState, CharacterAnimation>(biscuitProfile.Animations));
        }
        else
        {
            EnsureRequiredCharacter("dog", "Dog", "Dog Companion", "Energetic, encouraging, and alert companion.");
            EnsureRequiredCharacter("biscuit", "Biscuit", "Dog Companion", "Energetic, encouraging, and alert companion.");
        }

        // Legacy character slot preserved for backward compatibility
        EnsureRequiredCharacter("mochi", "Mochi", "Cat Companion (Legacy)", "Quiet, observant, and playful feline companion.");

        // Ensure 6 approved mockup characters (Spec 11 §1-2) are registered in the character system
        EnsureRequiredCharacter("character-03-astronaut-cat", "Astronaut Cat", "Astronaut Cat", "Compact dark cat in a space suit. Calm, curious, observant, quietly playful.");
        EnsureRequiredCharacter("character-04-knight", "Knight", "Armored Knight", "Compact armored knight with visor and plume. Stoic, disciplined, brave.");
        EnsureRequiredCharacter("character-05-orange-astronaut-cat", "Orange Astronaut Cat", "Orange Astronaut Cat", "Cheerful orange cat in space suit. Friendly, curious, optimistic.");
        EnsureRequiredCharacter("character-06-goth-girl", "Goth Girl", "Human Female", "Quiet, reserved, introspective, composed, slightly mysterious.");
        EnsureRequiredCharacter("character-07-retro-boy", "Retro Boy", "Human Male", "Relaxed, casual, grounded, slightly aloof but approachable.");
        EnsureRequiredCharacter("character-08-red-cap-adventurer", "Red-Cap Adventurer", "Adventurer", "Energetic, adventurous, expressive, optimistic and physical.");
    }

    private void RegisterAuthoritativeIdentities()
    {
        // 1. Niki (Retained Human Anime)
        _identities["niki"] = new CharacterIdentityProfile(
            "niki",
            "Niki",
            "Human Anime Companion",
            "Primary AI operator. Calm, focused, helpful, and concise.",
            CharacterCapability.HeadTilt | CharacterCapability.LookAround | CharacterCapability.Blink |
            CharacterCapability.Nod | CharacterCapability.Smile | CharacterCapability.Hands |
            CharacterCapability.Arms | CharacterCapability.Torso | CharacterCapability.Legs |
            CharacterCapability.WeightShift,
            ExpressionTheme.StandardAnime,
            MotionPrimitive.Nod,
            "Thoughtful nod followed by a subtle focus spark.",
            new[] { PetMood.Calm, PetMood.Focused, PetMood.Happy, PetMood.Tired });

        // 2. Dog / Biscuit (Retained Dog Companion)
        var dogProfile = new CharacterIdentityProfile(
            "dog",
            "Dog",
            "Dog Companion",
            "Energetic, encouraging, and alert companion.",
            CharacterCapability.FloppyEars | CharacterCapability.Tail | CharacterCapability.Forepaws |
            CharacterCapability.Legs | CharacterCapability.Torso | CharacterCapability.Bounce |
            CharacterCapability.Stretch | CharacterCapability.WeightShift | CharacterCapability.HeadTilt,
            ExpressionTheme.Energetic,
            MotionPrimitive.TailWag,
            "Cheerful bounce followed by a wagging tail.",
            new[] { PetMood.Happy, PetMood.Excited, PetMood.Playful, PetMood.Calm, PetMood.Sleepy });
        _identities["dog"] = dogProfile;

        // 3. Character 03 - Astronaut Cat (Spec 11 §2)
        _identities["character-03-astronaut-cat"] = new CharacterIdentityProfile(
            "character-03-astronaut-cat",
            "Astronaut Cat",
            "Astronaut Cat",
            "Calm, curious, observant, quietly playful, slightly spacey.",
            CharacterCapability.CatEars | CharacterCapability.Tail | CharacterCapability.Helmet |
            CharacterCapability.Antenna | CharacterCapability.Forepaws | CharacterCapability.Legs |
            CharacterCapability.Torso | CharacterCapability.HeadTilt | CharacterCapability.Blink |
            CharacterCapability.Stretch | CharacterCapability.WeightShift,
            ExpressionTheme.CelestialQuiet,
            MotionPrimitive.HeadTiltLeft,
            "Curious head tilt followed by a slow blink and tail curl.",
            new[] { PetMood.Calm, PetMood.Curious, PetMood.Sleepy, PetMood.Focused, PetMood.Playful });

        // 4. Character 04 - Knight (Spec 11 §2)
        _identities["character-04-knight"] = new CharacterIdentityProfile(
            "character-04-knight",
            "Knight",
            "Armored Knight",
            "Stoic, disciplined, brave, duty-oriented, dignified.",
            CharacterCapability.Helmet | CharacterCapability.Visor | CharacterCapability.Plume |
            CharacterCapability.Armor | CharacterCapability.Sword | CharacterCapability.Hands |
            CharacterCapability.Arms | CharacterCapability.Legs | CharacterCapability.Torso |
            CharacterCapability.GuardStance | CharacterCapability.WeightShift | CharacterCapability.Nod,
            ExpressionTheme.Heraldic,
            MotionPrimitive.GuardStance,
            "Controlled armor/weapon check followed by a disciplined guard stance.",
            new[] { PetMood.Focused, PetMood.Calm, PetMood.Tired, PetMood.Surprised, PetMood.Happy });

        // 5. Character 05 - Orange Astronaut Cat (Spec 11 §2)
        _identities["character-05-orange-astronaut-cat"] = new CharacterIdentityProfile(
            "character-05-orange-astronaut-cat",
            "Orange Astronaut Cat",
            "Orange Astronaut Cat",
            "Cheerful, energetic, friendly, curious, playful, optimistic.",
            CharacterCapability.CatEars | CharacterCapability.Tail | CharacterCapability.Helmet |
            CharacterCapability.Forepaws | CharacterCapability.Arms | CharacterCapability.Legs |
            CharacterCapability.Torso | CharacterCapability.Bounce | CharacterCapability.Stretch |
            CharacterCapability.Smile,
            ExpressionTheme.CelestialEnergetic,
            MotionPrimitive.Bounce,
            "Quick bounce with tail movement and cheerful paw gesture.",
            new[] { PetMood.Happy, PetMood.Excited, PetMood.Curious, PetMood.Playful, PetMood.Sleepy });

        // 6. Character 06 - Goth Girl (Spec 11 §2)
        _identities["character-06-goth-girl"] = new CharacterIdentityProfile(
            "character-06-goth-girl",
            "Goth Girl",
            "Human Female",
            "Quiet, reserved, introspective, composed, slightly mysterious.",
            CharacterCapability.HairGroups | CharacterCapability.Hands | CharacterCapability.Arms |
            CharacterCapability.Torso | CharacterCapability.Legs | CharacterCapability.HeadTilt |
            CharacterCapability.Blink | CharacterCapability.WeightShift | CharacterCapability.Smile,
            ExpressionTheme.DarkReserved,
            MotionPrimitive.SlowBlink,
            "Hair adjustment followed by looking away and a slow blink.",
            new[] { PetMood.Calm, PetMood.Tired, PetMood.Confused, PetMood.Focused, PetMood.Disappointed });

        // 7. Character 07 - Retro Boy (Spec 11 §2)
        _identities["character-07-retro-boy"] = new CharacterIdentityProfile(
            "character-07-retro-boy",
            "Retro Boy",
            "Human Male",
            "Relaxed, casual, grounded, slightly aloof but approachable.",
            CharacterCapability.HairGroups | CharacterCapability.Jacket | CharacterCapability.Hands |
            CharacterCapability.Arms | CharacterCapability.Torso | CharacterCapability.Legs |
            CharacterCapability.HeadTilt | CharacterCapability.Nod | CharacterCapability.WeightShift |
            CharacterCapability.Smile,
            ExpressionTheme.CasualRelaxed,
            MotionPrimitive.Nod,
            "Casual sleeve adjustment followed by a relaxed head nod.",
            new[] { PetMood.Calm, PetMood.Curious, PetMood.Playful, PetMood.Sleepy, PetMood.Focused });

        // 8. Character 08 - Red-Cap Adventurer (Spec 11 §2)
        _identities["character-08-red-cap-adventurer"] = new CharacterIdentityProfile(
            "character-08-red-cap-adventurer",
            "Red-Cap Adventurer",
            "Adventurer",
            "Energetic, adventurous, expressive, optimistic and physical.",
            CharacterCapability.Cap | CharacterCapability.Overalls | CharacterCapability.Hands |
            CharacterCapability.Arms | CharacterCapability.Torso | CharacterCapability.Legs |
            CharacterCapability.Bounce | CharacterCapability.FistPump | CharacterCapability.Smile,
            ExpressionTheme.Adventurous,
            MotionPrimitive.FistPump,
            "Quick cap adjustment followed by a celebratory fist pump.",
            new[] { PetMood.Excited, PetMood.Happy, PetMood.Curious, PetMood.Playful, PetMood.Surprised });

        // Legacy slot: Mochi (preserved but hidden/deprecated)
        _legacyMochiIdentity = new CharacterIdentityProfile(
            "mochi",
            "Mochi",
            "Cat Companion (Legacy)",
            "Quiet, observant, and playful feline companion.",
            CharacterCapability.CatEars | CharacterCapability.Tail | CharacterCapability.Forepaws |
            CharacterCapability.Legs | CharacterCapability.Torso | CharacterCapability.HeadTilt |
            CharacterCapability.Stretch,
            ExpressionTheme.Feline,
            MotionPrimitive.HeadTiltLeft,
            "Quiet head tilt and feline stretch.",
            new[] { PetMood.Curious, PetMood.Calm, PetMood.Playful, PetMood.Sleepy });
    }

    private void EnsureRequiredCharacter(string id, string name, string species, string desc)
    {
        if (!_characters.ContainsKey(id))
        {
            _characters[id] = CreateFallbackProfile(id, name, species, desc);
            _logger?.LogDebug("Created default profile for character '{Id}'", id);
        }
    }

    private CharacterIdentityProfile CreateDefaultIdentityFor(CharacterProfile profile)
    {
        return new CharacterIdentityProfile(
            profile.Id,
            profile.DisplayName,
            profile.Species,
            profile.Description,
            CharacterCapability.HeadTilt | CharacterCapability.Blink | CharacterCapability.Nod | CharacterCapability.WeightShift,
            ExpressionTheme.StandardAnime,
            MotionPrimitive.Nod,
            "Gentle nod.",
            new[] { PetMood.Calm, PetMood.Happy, PetMood.Focused });
    }

    private CharacterProfile? LoadProfileFromManifest(string charDir, string manifestPath)
    {
        var jsonText = File.ReadAllText(manifestPath);
        using var doc = JsonDocument.Parse(jsonText);
        var root = doc.RootElement;

        var id = root.GetProperty("id").GetString() ?? Path.GetFileName(charDir);
        var displayName = root.GetProperty("displayName").GetString() ?? id;
        var species = root.TryGetProperty("species", out var sp) ? sp.GetString() ?? "" : "";
        var desc = root.TryGetProperty("description", out var ds) ? ds.GetString() ?? "" : "";

        var animations = new Dictionary<CharacterState, CharacterAnimation>();

        if (root.TryGetProperty("animations", out var animsElem) && animsElem.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in animsElem.EnumerateObject())
            {
                if (Enum.TryParse<CharacterState>(prop.Name, ignoreCase: true, out var state))
                {
                    var animObj = prop.Value;
                    var durationMs = animObj.TryGetProperty("durationMs", out var dur) ? dur.GetInt32() : 300;
                    var isLooping = !animObj.TryGetProperty("isLooping", out var loop) || loop.GetBoolean();

                    var frames = new List<SpriteFrame>();
                    if (animObj.TryGetProperty("frames", out var framesElem) && framesElem.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var f in framesElem.EnumerateArray())
                        {
                            var frameFile = f.GetString();
                            if (!string.IsNullOrEmpty(frameFile))
                            {
                                var fullFramePath = Path.Combine(charDir, frameFile);
                                frames.Add(new SpriteFrame(fullFramePath, durationMs, 48, 48));
                            }
                        }
                    }

                    if (frames.Count > 0)
                    {
                        animations[state] = new CharacterAnimation(state, frames, isLooping);
                    }
                }
            }
        }

        // Guarantee Idle animation exists
        if (!animations.ContainsKey(CharacterState.Idle))
        {
            animations[CharacterState.Idle] = new CharacterAnimation(CharacterState.Idle, new[]
            {
                new SpriteFrame(Path.Combine(charDir, "idle_0.png"), 400, 48, 48)
            });
        }

        return new CharacterProfile(id, displayName, species, desc, animations);
    }

    private CharacterProfile CreateFallbackProfile(string id, string name, string species, string desc)
    {
        var animations = new Dictionary<CharacterState, CharacterAnimation>();
        var requiredStates = new[]
        {
            CharacterState.Idle,
            CharacterState.Listening,
            CharacterState.Thinking,
            CharacterState.Working,
            CharacterState.Happy,
            CharacterState.Notification,
            CharacterState.Sleep
        };

        foreach (var state in requiredStates)
        {
            var frames = new List<SpriteFrame>
            {
                new SpriteFrame($"assets/characters/{id}/{state.ToString().ToLowerInvariant()}_0.png", 350, 48, 48),
                new SpriteFrame($"assets/characters/{id}/{state.ToString().ToLowerInvariant()}_1.png", 350, 48, 48)
            };
            animations[state] = new CharacterAnimation(state, frames, isLooping: true);
        }

        return new CharacterProfile(id, name, species, desc, animations);
    }
}
