using System.Text;
using Microsoft.Extensions.Logging;
using NikiAI.Core.Memory;

namespace NikiAI.Memory;

/// <summary>
/// Assembles explicit user memories and project notes into prompt context for AI model interactions.
/// Strictly enforces the MemoryEnabled privacy setting and context length limits.
/// </summary>
public class MemoryContextManager
{
    private readonly IMemoryService _memoryService;
    private readonly ILogger<MemoryContextManager>? _logger;

    public MemoryContextManager(IMemoryService memoryService, ILogger<MemoryContextManager>? logger = null)
    {
        _memoryService = memoryService ?? throw new ArgumentNullException(nameof(memoryService));
        _logger = logger;
    }

    /// <summary>
    /// Builds a formatted context block containing explicit user preferences and project knowledge.
    /// Returns empty string if MemoryEnabled is false in privacy settings.
    /// Does not duplicate conversation turns or include operational timeline events.
    /// </summary>
    public async Task<string> BuildMemoryContextPromptAsync(
        string? currentProjectId = null,
        int maxCharacters = 4000,
        CancellationToken cancellationToken = default)
    {
        if (!_memoryService.IsMemoryEnabled())
        {
            _logger?.LogDebug("Memory is disabled; returning empty memory context.");
            return string.Empty;
        }

        var longTermMemories = await _memoryService.GetMemoriesAsync(MemoryCategory.LongTerm, cancellationToken);
        IReadOnlyList<MemoryItem> projectMemories = Array.Empty<MemoryItem>();

        if (!string.IsNullOrWhiteSpace(currentProjectId))
        {
            projectMemories = await _memoryService.GetProjectMemoriesAsync(currentProjectId, cancellationToken);
        }

        if (longTermMemories.Count == 0 && projectMemories.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.AppendLine("<user_memory_context>");
        sb.AppendLine("The user has explicitly saved the following facts, preferences, and project context:");

        if (longTermMemories.Count > 0)
        {
            sb.AppendLine("[Preferences & User Facts]");
            foreach (var m in longTermMemories)
            {
                var line = $"- {m.Content}";
                if (sb.Length + line.Length > maxCharacters) break;
                sb.AppendLine(line);
            }
        }

        if (projectMemories.Count > 0)
        {
            sb.AppendLine("[Active Project Notes]");
            foreach (var m in projectMemories)
            {
                var line = $"- {m.Content}";
                if (sb.Length + line.Length > maxCharacters) break;
                sb.AppendLine(line);
            }
        }

        sb.AppendLine("</user_memory_context>");
        return sb.ToString();
    }
}
