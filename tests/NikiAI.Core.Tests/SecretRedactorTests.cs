using NikiAI.Core.Logging;

namespace NikiAI.Core.Tests;

public class SecretRedactorTests
{
    [Fact]
    public void Redact_OpenAiApiKey_ShouldBeMasked()
    {
        var input = "Sending request with authorization key sk-1234567890abcdef1234567890abcdef to endpoint.";
        var redacted = SecretRedactor.Redact(input);

        Assert.DoesNotContain("sk-1234567890abcdef1234567890abcdef", redacted);
        Assert.Contains(SecretRedactor.RedactedMask, redacted);
    }

    [Fact]
    public void Redact_GeminiApiKey_ShouldBeMasked()
    {
        var input = "Google Gemini Key AIzaSyD1234567890abcdef1234567890abcde loaded.";
        var redacted = SecretRedactor.Redact(input);

        Assert.DoesNotContain("AIzaSyD1234567890abcdef1234567890abcde", redacted);
        Assert.Contains(SecretRedactor.RedactedMask, redacted);
    }

    [Fact]
    public void Redact_BearerToken_ShouldBeMasked()
    {
        var input = "Header: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.xyz";
        var redacted = SecretRedactor.Redact(input);

        Assert.DoesNotContain("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.xyz", redacted);
        Assert.Contains(SecretRedactor.RedactedMask, redacted);
    }

    [Fact]
    public void Redact_JsonSecretFields_ShouldBeMasked()
    {
        var input = @"{ ""api_key"": ""my-super-secret-password-123"", ""model"": ""gpt-4"" }";
        var redacted = SecretRedactor.Redact(input);

        Assert.DoesNotContain("my-super-secret-password-123", redacted);
        Assert.Contains(@"""api_key"": ""[REDACTED]""", redacted);
        Assert.Contains(@"""model"": ""gpt-4""", redacted);
    }

    [Fact]
    public void Redact_ExplicitSecretValue_ShouldBeMasked()
    {
        var explicitSecret = "custom-ultra-secret-token";
        var input = $"User session with {explicitSecret} initialized.";
        var redacted = SecretRedactor.Redact(input, [explicitSecret]);

        Assert.DoesNotContain(explicitSecret, redacted);
        Assert.Contains(SecretRedactor.RedactedMask, redacted);
    }

    [Fact]
    public void Redact_NormalTextWithoutSecrets_RemainsUnchanged()
    {
        var input = "Application started on port 5000. All services healthy.";
        var redacted = SecretRedactor.Redact(input);

        Assert.Equal(input, redacted);
    }
}
