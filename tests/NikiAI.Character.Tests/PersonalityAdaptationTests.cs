using NikiAI.Character;
using NikiAI.Core.Character;
using NikiAI.Core.Security;
using Xunit;

namespace NikiAI.Character.Tests;

public class PersonalityAdaptationTests
{
    private class FakeSecureSettingsStore : ISecureSettingsStore
    {
        private readonly Dictionary<string, string> _store = new();

        public Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(_store.TryGetValue(key, out var val) ? val : null);

        public Task SetSecretAsync(string key, string secret, CancellationToken cancellationToken = default)
        {
            _store[key] = secret;
            return Task.CompletedTask;
        }

        public Task DeleteSecretAsync(string key, CancellationToken cancellationToken = default)
        {
            _store.Remove(key);
            return Task.CompletedTask;
        }

        public Task<bool> HasSecretAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(_store.ContainsKey(key));
    }

    [Fact]
    public void DisabledByDefault_ContractEnforced()
    {
        var store = new FakeSecureSettingsStore();
        var service = new PersonalityAdaptationService(store);

        Assert.False(service.IsEnabled);
    }

    [Fact]
    public void Disabled_RecordInteraction_PerformsZeroMetricCollection()
    {
        var store = new FakeSecureSettingsStore();
        var service = new PersonalityAdaptationService(store);

        service.RecordInteraction("niki");
        service.RecordSessionMinutes("niki", 60);

        var profile = service.GetProfile("niki");
        Assert.Equal(0, profile.InteractionCount);
        Assert.Equal(0, profile.TotalFocusSessionMinutes);
    }

    [Fact]
    public void Disabled_FamiliarityMultiplier_StrictlyReturnsUnitOne()
    {
        var store = new FakeSecureSettingsStore();
        var service = new PersonalityAdaptationService(store);

        var multiplier = service.GetFamiliarityWeightMultiplier("niki", MotionPrimitive.Smile);
        Assert.Equal(1.0, multiplier);
    }

    [Fact]
    public void SetEnabledFalse_PurgesStoredFamiliarityMetrics()
    {
        var store = new FakeSecureSettingsStore();
        var service = new PersonalityAdaptationService(store);

        // Temporarily enable to write data
        service.SetEnabled(true);
        Assert.True(service.IsEnabled);
        service.RecordInteraction("niki");

        var profileBefore = service.GetProfile("niki");
        Assert.Equal(1, profileBefore.InteractionCount);

        // Turn OFF -> Must purge and zero out
        service.SetEnabled(false);
        Assert.False(service.IsEnabled);

        var profileAfter = service.GetProfile("niki");
        Assert.Equal(0, profileAfter.InteractionCount);
    }
}
