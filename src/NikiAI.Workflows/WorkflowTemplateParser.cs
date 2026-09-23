using System.Text.RegularExpressions;

namespace NikiAI.Workflows;

/// <summary>
/// Safe template string substitution without dynamic script evaluation.
/// Replaces tokens such as {{inputs.key}} or {{key}} with provided string dictionary values.
/// </summary>
public static class WorkflowTemplateParser
{
    private static readonly Regex TokenRegex = new(@"\{\{\s*(?:inputs\.)?([a-zA-Z0-9_\-\.]+)\s*\}\}", RegexOptions.Compiled);

    public static string Substitute(string? template, IReadOnlyDictionary<string, string>? values)
    {
        if (string.IsNullOrEmpty(template) || values == null || values.Count == 0)
        {
            return template ?? string.Empty;
        }

        return TokenRegex.Replace(template, match =>
        {
            var key = match.Groups[1].Value;
            if (values.TryGetValue(key, out var val))
            {
                return val;
            }
            return string.Empty;
        });
    }
}
