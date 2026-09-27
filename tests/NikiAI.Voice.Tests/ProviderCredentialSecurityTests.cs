using NikiAI.Core.Logging;
using NikiAI.Core.Security;
using NikiAI.Core.Voice;
using NikiAI.Security;
using Xunit;

namespace NikiAI.Voice.Tests;

public class ProviderCredentialSecurityTests
{
    private class InMemorySecureSettingsStore : ISecureSettingsStore
    {
        private readonly Dictionary<string, string> _secrets = new();

        public Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default)
        {
            _secrets.TryGetValue(key, out var val);
            return Task.FromResult<string?>(val);
        }

        public Task SetSecretAsync(string key, string value, CancellationToken cancellationToken = default)
        {
            _secrets[key] = value;
            return Task.CompletedTask;
        }

        public Task DeleteSecretAsync(string key, CancellationToken cancellationToken = default)
        {
            _secrets.Remove(key);
            return Task.CompletedTask;
        }

        public Task<bool> HasSecretAsync(string key, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_secrets.ContainsKey(key));
        }

        public Task<IReadOnlyList<string>> ListSecretKeysAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<string>>(_secrets.Keys.ToList());
        }
    }

    [Fact]
    public async Task ProviderScopedCredentials_AreIsolatedByKey()
    {
        var store = new InMemorySecureSettingsStore();

        var ttsKeyOpenAi = "tts_credential_openai-tts";
        var ttsKeyCustom = "tts_credential_custom-tts";
        var visionKeyOpenAi = "vision_credential_openai-vision";

        await store.SetSecretAsync(ttsKeyOpenAi, "sk-tts-secret-123");
        await store.SetSecretAsync(ttsKeyCustom, "sk-custom-secret-456");
        await store.SetSecretAsync(visionKeyOpenAi, "sk-vision-secret-789");

        var val1 = await store.GetSecretAsync(ttsKeyOpenAi);
        var val2 = await store.GetSecretAsync(ttsKeyCustom);
        var val3 = await store.GetSecretAsync(visionKeyOpenAi);

        Assert.Equal("sk-tts-secret-123", val1);
        Assert.Equal("sk-custom-secret-456", val2);
        Assert.Equal("sk-vision-secret-789", val3);

        // Update one and verify others remain unchanged
        await store.SetSecretAsync(ttsKeyOpenAi, "sk-tts-secret-MODIFIED");
        Assert.Equal("sk-tts-secret-MODIFIED", await store.GetSecretAsync(ttsKeyOpenAi));
        Assert.Equal("sk-custom-secret-456", await store.GetSecretAsync(ttsKeyCustom));
        Assert.Equal("sk-vision-secret-789", await store.GetSecretAsync(visionKeyOpenAi));
    }

    [Fact]
    public void SecretRedactor_RedactsProviderCredentialsInLogsAndExceptions()
    {
        var rawApiKey = "sk-live-abcdef1234567890abcdef1234567890";
        var logMessage = $"Error connecting to provider with Authorization: Bearer {rawApiKey}";

        var redacted = SecretRedactor.Redact(logMessage);

        Assert.DoesNotContain(rawApiKey, redacted);
        Assert.Contains("[REDACTED", redacted);
    }

    [Fact]
    public void VoiceDescriptorAndConfig_DoNotExposeSecrets()
    {
        var descriptor = new VoiceDescriptor(
            Id: "nova",
            Name: "Nova",
            Gender: "Female",
            Locale: "en-US",
            ProviderId: "openai-tts"
        );

        var config = new TtsProviderConfig
        {
            ProviderId = "openai-tts",
            EndpointUrl = "https://api.openai.com/v1/audio/speech",
            ModelName = "tts-1",
            VoiceId = "nova",
            Speed = 1.0
        };

        // Neither VoiceDescriptor nor TtsProviderConfig should have any API key property
        var descriptorProperties = typeof(VoiceDescriptor).GetProperties().Select(p => p.Name.ToLowerInvariant());
        var configProperties = typeof(TtsProviderConfig).GetProperties().Select(p => p.Name.ToLowerInvariant());

        Assert.DoesNotContain("apikey", descriptorProperties);
        Assert.DoesNotContain("secret", descriptorProperties);
        Assert.DoesNotContain("password", descriptorProperties);

        Assert.DoesNotContain("apikey", configProperties);
        Assert.DoesNotContain("secret", configProperties);
        Assert.DoesNotContain("password", configProperties);
    }
}
