using NikiAI.Security;

namespace NikiAI.Security.Tests;

public class DpapiSecureSettingsStoreTests : IDisposable
{
    private readonly string _tempFile;

    public DpapiSecureSettingsStoreTests()
    {
        _tempFile = Path.Combine(Path.GetTempPath(), $"niki_test_{Guid.NewGuid():N}.dat");
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile))
        {
            try { File.Delete(_tempFile); } catch { }
        }
    }

    [Fact]
    public async Task SecureSettings_RoundTripSecret_SuccessfullyEncryptsAndDecrypts()
    {
        var store = new DpapiSecureSettingsStore(_tempFile);
        var key = "openai_api_key";
        var secret = "sk-test-secret-value-1234567890abcdef";

        await store.SetSecretAsync(key, secret);
        var retrieved = await store.GetSecretAsync(key);

        Assert.Equal(secret, retrieved);

        // Verification: Verify the file on disk does NOT contain the plaintext secret string
        var rawDiskBytes = await File.ReadAllBytesAsync(_tempFile);
        var rawDiskString = System.Text.Encoding.UTF8.GetString(rawDiskBytes);

        if (OperatingSystem.IsWindows())
        {
            Assert.DoesNotContain(secret, rawDiskString);
        }
    }

    [Fact]
    public async Task SecureSettings_HasSecret_And_DeleteSecret_WorkCorrectly()
    {
        var store = new DpapiSecureSettingsStore(_tempFile);
        var key = "gemini_key";
        var secret = "AIzaSyTestKey1234567890abcdef";

        Assert.False(await store.HasSecretAsync(key));

        await store.SetSecretAsync(key, secret);
        Assert.True(await store.HasSecretAsync(key));

        await store.DeleteSecretAsync(key);
        Assert.False(await store.HasSecretAsync(key));
        Assert.Null(await store.GetSecretAsync(key));
    }
}
