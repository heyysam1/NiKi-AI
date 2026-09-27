using NikiAI.Agent;
using NikiAI.Core.Security;
using Xunit;

namespace NikiAI.Agent.Tests;

public class ProviderCredentialManagerTests
{
    private class InMemorySecureSettingsStore : ISecureSettingsStore
    {
        private readonly Dictionary<string, string> _secrets = new();

        public Task SetSecretAsync(string key, string secret, CancellationToken cancellationToken = default)
        {
            _secrets[key] = secret;
            return Task.CompletedTask;
        }

        public Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default)
        {
            _secrets.TryGetValue(key, out var secret);
            return Task.FromResult<string?>(secret);
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
    }

    [Fact]
    public async Task SaveAndGetApiKey_RoundtripsCorrectly()
    {
        var store = new InMemorySecureSettingsStore();
        var manager = new ProviderCredentialManager(store);

        await manager.SaveApiKeyAsync("test_provider", "sk-secret-key-12345");
        var retrieved = await manager.GetApiKeyAsync("test_provider");
        var hasSecret = await manager.HasApiKeyAsync("test_provider");

        Assert.True(hasSecret);
        Assert.Equal("sk-secret-key-12345", retrieved);

        // Sync wrappers
        Assert.Equal("sk-secret-key-12345", manager.GetApiKey("test_provider"));
    }

    [Fact]
    public async Task SaveApiKey_WhenEmpty_DeletesExistingKey()
    {
        var store = new InMemorySecureSettingsStore();
        var manager = new ProviderCredentialManager(store);

        await manager.SaveApiKeyAsync("test_provider", "sk-secret-key-12345");
        await manager.SaveApiKeyAsync("test_provider", "");

        var hasSecret = await manager.HasApiKeyAsync("test_provider");
        var retrieved = await manager.GetApiKeyAsync("test_provider");

        Assert.False(hasSecret);
        Assert.Null(retrieved);
    }

    [Theory]
    [InlineData(null, "(Not configured)")]
    [InlineData("", "(Not configured)")]
    [InlineData("   ", "(Not configured)")]
    [InlineData("12345", "••••••••")]
    [InlineData("12345678", "••••••••")]
    [InlineData("sk-proj-1234567890abcdef", "sk-p••••••••cdef")]
    public void MaskApiKey_ProducesCorrectMaskedRepresentation(string? input, string expected)
    {
        var masked = ProviderCredentialManager.MaskApiKey(input);
        Assert.Equal(expected, masked);
    }
}
