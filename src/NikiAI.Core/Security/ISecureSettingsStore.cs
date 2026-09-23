namespace NikiAI.Core.Security;

/// <summary>
/// Contract for securely storing sensitive settings and API credentials.
/// Implemented using Windows DPAPI to prevent plaintext secret exposure.
/// </summary>
public interface ISecureSettingsStore
{
    Task SetSecretAsync(string key, string secretValue, CancellationToken cancellationToken = default);
    Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default);
    Task<bool> HasSecretAsync(string key, CancellationToken cancellationToken = default);
    Task DeleteSecretAsync(string key, CancellationToken cancellationToken = default);
}
