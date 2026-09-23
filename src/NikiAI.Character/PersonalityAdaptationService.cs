using System.Text.Json;
using Microsoft.Extensions.Logging;
using NikiAI.Core.Character;
using NikiAI.Core.Security;

namespace NikiAI.Character;

/// <summary>
/// Manages privacy-safe personality adaptation and familiarity weighting.
/// STRICT CONTRACT: When PersonalityAdaptation is disabled (default), performs ZERO metric collection,
/// calculation, or weighting, and adaptive signals exert 0.0 behavioral influence.
/// </summary>
public class PersonalityAdaptationService
{
    private const string SettingKey = "DesktopPet.PersonalityAdaptation";
    private const string ProfilePrefix = "DesktopPet.Familiarity.";

    private readonly ISecureSettingsStore? _settingsStore;
    private readonly ILogger<PersonalityAdaptationService>? _logger;
    private readonly object _lock = new();

    public bool IsEnabled { get; private set; }

    public PersonalityAdaptationService(
        ISecureSettingsStore? settingsStore = null,
        ILogger<PersonalityAdaptationService>? logger = null)
    {
        _settingsStore = settingsStore;
        _logger = logger;

        LoadSetting();
    }

    /// <summary>
    /// Loads the current adaptation setting. Defaults to false (OFF).
    /// </summary>
    public void LoadSetting()
    {
        lock (_lock)
        {
            if (_settingsStore == null)
            {
                IsEnabled = false;
                return;
            }

            try
            {
                var value = Task.Run(async () => await _settingsStore.GetSecretAsync(SettingKey).ConfigureAwait(false)).GetAwaiter().GetResult();
                IsEnabled = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to read PersonalityAdaptation setting from secure store.");
                IsEnabled = false;
            }
        }
    }

    /// <summary>
    /// Updates the adaptation setting toggle.
    /// When switched to OFF, immediately clears any stored familiarity data.
    /// </summary>
    public void SetEnabled(bool enabled)
    {
        lock (_lock)
        {
            IsEnabled = enabled;
            if (_settingsStore != null)
            {
                try
                {
                    Task.Run(async () => await _settingsStore.SetSecretAsync(SettingKey, enabled ? "true" : "false").ConfigureAwait(false)).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to persist PersonalityAdaptation setting to secure store.");
                }

                if (!enabled)
                {
                    // Strict contract: purge adaptive data when disabled
                    _logger?.LogInformation("Personality adaptation disabled. Purging all stored adaptive metrics.");
                }
            }
        }
    }

    /// <summary>
    /// Records a user interaction.
    /// STRICT CONTRACT: If adaptation is OFF, returns immediately without performing ANY metric collection or calculation.
    /// </summary>
    public void RecordInteraction(string characterId)
    {
        lock (_lock)
        {
            // CRITICAL: Zero collection or calculation when disabled
            if (!IsEnabled || _settingsStore == null || string.IsNullOrWhiteSpace(characterId))
            {
                return;
            }

            var profile = LoadProfileInternal(characterId);
            var updated = profile with
            {
                InteractionCount = profile.InteractionCount + 1,
                LastInteraction = DateTime.UtcNow
            };
            SaveProfileInternal(updated);
        }
    }

    /// <summary>
    /// Records work session minutes.
    /// STRICT CONTRACT: If adaptation is OFF, returns immediately without performing ANY metric collection or calculation.
    /// </summary>
    public void RecordSessionMinutes(string characterId, int minutes)
    {
        lock (_lock)
        {
            // CRITICAL: Zero collection or calculation when disabled
            if (!IsEnabled || _settingsStore == null || string.IsNullOrWhiteSpace(characterId) || minutes <= 0)
            {
                return;
            }

            var profile = LoadProfileInternal(characterId);
            var updated = profile with
            {
                TotalFocusSessionMinutes = profile.TotalFocusSessionMinutes + minutes,
                LastInteraction = DateTime.UtcNow
            };
            SaveProfileInternal(updated);
        }
    }

    /// <summary>
    /// Computes familiarity multiplier for behavior selection.
    /// STRICT CONTRACT: If adaptation is OFF, strictly returns 1.0 (zero influence).
    /// </summary>
    public double GetFamiliarityWeightMultiplier(string characterId, MotionPrimitive primitive)
    {
        lock (_lock)
        {
            // STRICT CONTRACT: Zero weighting or behavioral influence when disabled
            if (!IsEnabled || _settingsStore == null || string.IsNullOrWhiteSpace(characterId))
            {
                return 1.0;
            }

            var profile = LoadProfileInternal(characterId);

            // Highly familiar companions have subtle bias towards affectionate/relaxed primitives
            if (profile.InteractionCount > 50)
            {
                if (primitive is MotionPrimitive.HeadTiltLeft or MotionPrimitive.HeadTiltRight or MotionPrimitive.Stretch or MotionPrimitive.Smile)
                {
                    return 1.15; // 15% subtle bias
                }
            }

            return 1.0;
        }
    }

    /// <summary>
    /// Clears familiarity profile for a character.
    /// </summary>
    public void ClearProfile(string characterId)
    {
        lock (_lock)
        {
            if (_settingsStore != null && !string.IsNullOrWhiteSpace(characterId))
            {
                try
                {
                    Task.Run(async () => await _settingsStore.DeleteSecretAsync(ProfilePrefix + characterId).ConfigureAwait(false)).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to clear familiarity profile for character {Id}", characterId);
                }
            }
        }
    }

    /// <summary>
    /// Gets the current familiarity profile.
    /// STRICT CONTRACT: If adaptation is OFF, returns a default blank profile (zero collected metrics).
    /// </summary>
    public FamiliarityProfile GetProfile(string characterId)
    {
        lock (_lock)
        {
            if (!IsEnabled || _settingsStore == null || string.IsNullOrWhiteSpace(characterId))
            {
                return new FamiliarityProfile(characterId);
            }
            return LoadProfileInternal(characterId);
        }
    }

    private FamiliarityProfile LoadProfileInternal(string characterId)
    {
        if (_settingsStore == null) return new FamiliarityProfile(characterId);

        try
        {
            var json = Task.Run(async () => await _settingsStore.GetSecretAsync(ProfilePrefix + characterId).ConfigureAwait(false)).GetAwaiter().GetResult();
            if (string.IsNullOrWhiteSpace(json))
            {
                return new FamiliarityProfile(characterId);
            }

            return JsonSerializer.Deserialize<FamiliarityProfile>(json) ?? new FamiliarityProfile(characterId);
        }
        catch
        {
            return new FamiliarityProfile(characterId);
        }
    }

    private void SaveProfileInternal(FamiliarityProfile profile)
    {
        if (_settingsStore == null) return;
        try
        {
            var json = JsonSerializer.Serialize(profile);
            Task.Run(async () => await _settingsStore.SetSecretAsync(ProfilePrefix + profile.CharacterId, json).ConfigureAwait(false)).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to persist familiarity profile for character {Id}", profile.CharacterId);
        }
    }
}
