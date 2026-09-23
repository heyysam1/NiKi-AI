using NikiAI.Core.Agent;
using NikiAI.Core.Security;

namespace NikiAI.Agent;

/// <summary>
/// Manages provider credentials and encryption using ISecureSettingsStore (Windows DPAPI).
/// Prevents plaintext key exposure and provides safe UI masking.
/// </summary>
public class ProviderCredentialManager
{
    private readonly ISecureSettingsStore _secureStore;

    public ProviderCredentialManager(ISecureSettingsStore secureStore)
    {
        _secureStore = secureStore ?? throw new ArgumentNullException(nameof(secureStore));
    }

    public async Task SaveApiKeyAsync(string secretKeyName, string apiKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secretKeyName);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            await _secureStore.DeleteSecretAsync(secretKeyName, cancellationToken);
        }
        else
        {
            await _secureStore.SetSecretAsync(secretKeyName, apiKey.Trim(), cancellationToken);
        }
    }

    public async Task<string?> GetApiKeyAsync(string secretKeyName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secretKeyName);
        return await _secureStore.GetSecretAsync(secretKeyName, cancellationToken);
    }

    public async Task<bool> HasApiKeyAsync(string secretKeyName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secretKeyName);
        return await _secureStore.HasSecretAsync(secretKeyName, cancellationToken);
    }

    public string? GetApiKey(string secretKeyName) =>
        GetApiKeyAsync(secretKeyName).GetAwaiter().GetResult();

    public void SaveApiKey(string secretKeyName, string apiKey) =>
        SaveApiKeyAsync(secretKeyName, apiKey).GetAwaiter().GetResult();

    /// <summary>
    /// Masks an API key for safe UI display (e.g., "sk-ab...ef12").
    /// </summary>
    public static string MaskApiKey(string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return "(Not configured)";
        if (apiKey.Length <= 8) return "••••••••";

        var prefix = apiKey[..Math.Min(4, apiKey.Length)];
        var suffix = apiKey[^Math.Min(4, apiKey.Length)..];
        return $"{prefix}••••••••{suffix}";
    }
}
