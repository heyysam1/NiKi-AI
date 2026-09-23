using System.Text.RegularExpressions;

namespace NikiAI.Core.Logging;

/// <summary>
/// Utility for redacting sensitive secrets, API keys, tokens, and credentials from logs,
/// error messages, and task history to prevent accidental credential leakage.
/// </summary>
public static class SecretRedactor
{
    public const string RedactedMask = "[REDACTED]";

    private static readonly Regex[] SecretPatterns =
    [
        // OpenAI / Gemini / Anthropic / Generic API key patterns
        new(@"\b(sk-[a-zA-Z0-9_\-]{20,})\b", RegexOptions.Compiled),
        new(@"\b(AIza[a-zA-Z0-9_\-]{30,})\b", RegexOptions.Compiled),
        new(@"\b(ant-[a-zA-Z0-9_\-]{20,})\b", RegexOptions.Compiled),
        // Bearer tokens
        new(@"Bearer\s+[a-zA-Z0-9_\-\.]{20,}", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        // JSON key-value secrets
        new(@"""(?:api[_\-]?key|secret|password|token|access_token|private_key)""\s*:\s*""([^""]+)""", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        // Query param / assignment secrets
        new(@"(?:api[_\-]?key|secret|password|token|access_token)=([^&\s]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase)
    ];

    /// <summary>
    /// Redacts known secret patterns and any registered explicit secret values from the input text.
    /// </summary>
    public static string Redact(string? input, IEnumerable<string>? explicitSecrets = null)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        string result = input;

        // Redact registered explicit secrets first
        if (explicitSecrets != null)
        {
            foreach (var secret in explicitSecrets)
            {
                if (!string.IsNullOrWhiteSpace(secret) && secret.Length >= 4)
                {
                    result = result.Replace(secret, RedactedMask, StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        // Apply regex pattern redactors
        foreach (var pattern in SecretPatterns)
        {
            result = pattern.Replace(result, match =>
            {
                // If there is a capture group for the value part (e.g. JSON key-value or query param), redact only the value
                if (match.Groups.Count > 1)
                {
                    var valGroup = match.Groups[1];
                    return match.Value.Remove(valGroup.Index - match.Index, valGroup.Length)
                                      .Insert(valGroup.Index - match.Index, RedactedMask);
                }
                return RedactedMask;
            });
        }

        return result;
    }
}
