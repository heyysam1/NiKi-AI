using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NikiAI.Core.Security;

namespace NikiAI.Security;

/// <summary>
/// DPAPI-backed secure settings store implementing ISecureSettingsStore.
/// Stores credentials encrypted at rest using Windows Data Protection API (CurrentUser scope).
/// </summary>
public class DpapiSecureSettingsStore : ISecureSettingsStore
{
    private static readonly byte[] Entropy = "NikiAI.SecretEntropy.v1"u8.ToArray();
    private readonly string _storageFilePath;
    private readonly ILogger<DpapiSecureSettingsStore>? _logger;
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public DpapiSecureSettingsStore(string? customPath = null, ILogger<DpapiSecureSettingsStore>? logger = null)
    {
        _logger = logger;
        if (!string.IsNullOrWhiteSpace(customPath))
        {
            _storageFilePath = customPath;
        }
        else
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var folder = Path.Combine(localAppData, "NikiAI");
            Directory.CreateDirectory(folder);
            _storageFilePath = Path.Combine(folder, "secure_settings.dat");
        }
    }

    public async Task SetSecretAsync(string key, string secretValue, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(secretValue);

        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            var dict = await ReadStoreUnsafeAsync(cancellationToken);
            dict[key] = secretValue;
            await WriteStoreUnsafeAsync(dict, cancellationToken);
            _logger?.LogDebug("Secret key '{Key}' securely updated.", key);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            var dict = await ReadStoreUnsafeAsync(cancellationToken);
            return dict.TryGetValue(key, out var val) ? val : null;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<bool> HasSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            var dict = await ReadStoreUnsafeAsync(cancellationToken);
            return dict.ContainsKey(key);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task DeleteSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            var dict = await ReadStoreUnsafeAsync(cancellationToken);
            if (dict.Remove(key))
            {
                await WriteStoreUnsafeAsync(dict, cancellationToken);
                _logger?.LogDebug("Secret key '{Key}' removed from secure store.", key);
            }
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private async Task<Dictionary<string, string>> ReadStoreUnsafeAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_storageFilePath))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var encryptedBytes = await File.ReadAllBytesAsync(_storageFilePath, cancellationToken);
            if (encryptedBytes.Length == 0)
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            byte[] decryptedBytes;
            if (OperatingSystem.IsWindows())
            {
                decryptedBytes = ProtectedData.Unprotect(encryptedBytes, Entropy, DataProtectionScope.CurrentUser);
            }
            else
            {
                decryptedBytes = encryptedBytes;
            }

            var json = Encoding.UTF8.GetString(decryptedBytes);
            var result = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return result ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to decrypt secure settings store. Returning empty dictionary.");
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private async Task WriteStoreUnsafeAsync(Dictionary<string, string> dict, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(dict);
        var rawBytes = Encoding.UTF8.GetBytes(json);

        byte[] encryptedBytes;
        if (OperatingSystem.IsWindows())
        {
            encryptedBytes = ProtectedData.Protect(rawBytes, Entropy, DataProtectionScope.CurrentUser);
        }
        else
        {
            encryptedBytes = rawBytes;
        }

        var dir = Path.GetDirectoryName(_storageFilePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        await File.WriteAllBytesAsync(_storageFilePath, encryptedBytes, cancellationToken);
    }
}
