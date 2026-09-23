using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NikiAI.Core.Logging;
using NikiAI.Core.Memory;
using NikiAI.Core.Security;

namespace NikiAI.Memory;

/// <summary>
/// High-level orchestration service for the Niki AI Memory Subsystem.
/// Manages explicit long-term memory, project knowledge, timeline event logging,
/// memory export, and authoritative privacy controls.
/// </summary>
public class MemoryService : IMemoryService
{
    private const string MemoryEnabledKey = "privacy_memory_enabled";

    private readonly IMemoryStore _memoryStore;
    private readonly IProjectRepository _projectRepository;
    private readonly ITimelineRepository _timelineRepository;
    private readonly ISecureSettingsStore? _settingsStore;
    private readonly ILogger<MemoryService>? _logger;

    private bool _memoryEnabled = true;

    public MemoryService(
        IMemoryStore memoryStore,
        IProjectRepository? projectRepository = null,
        ITimelineRepository? timelineRepository = null,
        ISecureSettingsStore? settingsStore = null,
        ILogger<MemoryService>? logger = null)
    {
        _memoryStore = memoryStore ?? throw new ArgumentNullException(nameof(memoryStore));
        _projectRepository = projectRepository ?? new InMemoryProjectRepository();
        _timelineRepository = timelineRepository ?? new InMemoryTimelineRepository();
        _settingsStore = settingsStore;
        _logger = logger;

        LoadSettings();
    }

    private void LoadSettings()
    {
        if (_settingsStore != null)
        {
            try
            {
                var val = Task.Run(async () => await _settingsStore.GetSecretAsync(MemoryEnabledKey).ConfigureAwait(false)).GetAwaiter().GetResult();
                if (!string.IsNullOrWhiteSpace(val) && bool.TryParse(val, out var parsed))
                {
                    _memoryEnabled = parsed;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to load MemoryEnabled setting from secure store.");
            }
        }
    }

    /// <summary>
    /// Indicates whether memory persistence and prompt context injection are currently enabled by user privacy settings.
    /// </summary>
    public bool IsMemoryEnabled() => _memoryEnabled;

    /// <summary>
    /// Toggles the MemoryEnabled privacy setting.
    /// When set to false, blocks agent memory reads/writes and prompt memory injection.
    /// Existing stored memories remain manageable via user-facing controls.
    /// </summary>
    public async Task SetMemoryEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        _memoryEnabled = enabled;
        if (_settingsStore != null)
        {
            try
            {
                await _settingsStore.SetSecretAsync(MemoryEnabledKey, enabled.ToString(), cancellationToken);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to save MemoryEnabled setting to secure store.");
            }
        }
        _logger?.LogInformation("Privacy setting MemoryEnabled updated to {Enabled}", enabled);
    }

    #region Explicit Memory Operations

    /// <summary>
    /// Explicitly saves a user preference, fact, or project note.
    /// Requires explicit user intent; agent inference or passive conversation must never call this silently.
    /// </summary>
    public async Task<MemoryItem> SaveExplicitMemoryAsync(
        string content,
        MemoryCategory category = MemoryCategory.LongTerm,
        string? contextExplanation = null,
        string? projectId = null,
        bool isPinned = false,
        DateTimeOffset? expiresAt = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsMemoryEnabled())
        {
            throw new InvalidOperationException("Cannot save memory: Memory is disabled by privacy settings.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        var id = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        var item = new MemoryItem(
            Id: id,
            Category: category,
            Content: content.Trim(),
            CreatedAt: now,
            ContextExplanation: contextExplanation?.Trim(),
            UpdatedAt: now,
            ProjectId: projectId,
            IsPinned: isPinned,
            ExpiresAt: expiresAt
        );

        await _memoryStore.SaveMemoryAsync(item, cancellationToken);
        _logger?.LogInformation("Explicit memory saved ({Category}): {Id}", category, id);
        return item;
    }

    public Task<MemoryItem?> GetMemoryByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        return _memoryStore.GetMemoryByIdAsync(id, cancellationToken);
    }

    public Task<IReadOnlyList<MemoryItem>> GetMemoriesAsync(MemoryCategory category, CancellationToken cancellationToken = default)
    {
        return _memoryStore.GetMemoriesAsync(category, cancellationToken);
    }

    public async Task<IReadOnlyList<MemoryItem>> GetAllMemoriesAsync(CancellationToken cancellationToken = default)
    {
        var longTerm = await _memoryStore.GetMemoriesAsync(MemoryCategory.LongTerm, cancellationToken);
        var project = await _memoryStore.GetMemoriesAsync(MemoryCategory.Project, cancellationToken);
        return longTerm.Concat(project).ToList();
    }

    public Task<IReadOnlyList<MemoryItem>> SearchMemoriesAsync(string query, MemoryCategory? category = null, CancellationToken cancellationToken = default)
    {
        if (!IsMemoryEnabled())
        {
            return Task.FromResult<IReadOnlyList<MemoryItem>>(Array.Empty<MemoryItem>());
        }

        return _memoryStore.SearchMemoriesAsync(query, category, cancellationToken);
    }

    public Task DeleteMemoryAsync(string id, CancellationToken cancellationToken = default)
    {
        return _memoryStore.DeleteMemoryAsync(id, cancellationToken);
    }

    public Task ClearCategoryAsync(MemoryCategory category, CancellationToken cancellationToken = default)
    {
        return _memoryStore.ClearCategoryAsync(category, cancellationToken);
    }

    #endregion

    #region Project Memory Operations

    public Task<Project> CreateProjectAsync(string name, string? description = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var now = DateTimeOffset.UtcNow;
        var project = new Project(
            Id: Guid.NewGuid().ToString("N"),
            Name: name.Trim(),
            Description: description?.Trim(),
            CreatedAt: now,
            UpdatedAt: now
        );

        return _projectRepository.CreateProjectAsync(project, cancellationToken);
    }

    public Task<Project?> GetProjectByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        return _projectRepository.GetProjectByIdAsync(id, cancellationToken);
    }

    public Task<IReadOnlyList<Project>> GetAllProjectsAsync(CancellationToken cancellationToken = default)
    {
        return _projectRepository.GetAllProjectsAsync(cancellationToken);
    }

    public Task<IReadOnlyList<MemoryItem>> GetProjectMemoriesAsync(string projectId, CancellationToken cancellationToken = default)
    {
        return _memoryStore.GetProjectMemoriesAsync(projectId, cancellationToken);
    }

    public Task<bool> DeleteProjectAsync(string id, CancellationToken cancellationToken = default)
    {
        return _projectRepository.DeleteProjectAsync(id, cancellationToken);
    }

    #endregion

    #region Timeline Event Operations (Operational Milestone Log)

    public async Task LogTimelineEventAsync(
        string eventType,
        string source,
        string summary,
        string? detailsJson = null,
        string? relatedId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);

        var timelineEvent = new TimelineEvent(
            Id: Guid.NewGuid().ToString("N"),
            EventType: eventType.Trim(),
            Source: source.Trim(),
            Summary: summary.Trim(),
            DetailsJson: detailsJson,
            Timestamp: DateTimeOffset.UtcNow,
            RelatedId: relatedId
        );

        await _timelineRepository.LogEventAsync(timelineEvent, cancellationToken);
    }

    public Task<IReadOnlyList<TimelineEvent>> GetRecentTimelineEventsAsync(int limit = 50, CancellationToken cancellationToken = default)
    {
        return _timelineRepository.GetRecentEventsAsync(limit, cancellationToken);
    }

    public Task<int> ClearAllTimelineEventsAsync(CancellationToken cancellationToken = default)
    {
        return _timelineRepository.ClearAllEventsAsync(cancellationToken);
    }

    #endregion

    #region Export Operations (Explicit User-Triggered, Redacts Secrets)

    /// <summary>
    /// Exports explicit long-term and project memories to structured JSON or Markdown.
    /// Automatically applies SecretRedactor to ensure credentials and tokens are never exported.
    /// </summary>
    public async Task<string> ExportMemoriesAsync(string format = "json", CancellationToken cancellationToken = default)
    {
        var longTerm = await _memoryStore.GetMemoriesAsync(MemoryCategory.LongTerm, cancellationToken);
        var projects = await _projectRepository.GetAllProjectsAsync(cancellationToken);
        var projectMemories = await _memoryStore.GetMemoriesAsync(MemoryCategory.Project, cancellationToken);

        if (string.Equals(format, "markdown", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(format, "md", StringComparison.OrdinalIgnoreCase))
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Niki AI — Exported Memory Knowledge Base");
            sb.AppendLine($"Export Date: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine();

            sb.AppendLine("## Explicit Long-Term Memories & Preferences");
            if (longTerm.Count == 0) sb.AppendLine("_No long-term memories recorded._");
            foreach (var m in longTerm)
            {
                var content = SecretRedactor.Redact(m.Content);
                var exp = string.IsNullOrWhiteSpace(m.ContextExplanation) ? "" : $" (Context: {SecretRedactor.Redact(m.ContextExplanation)})";
                sb.AppendLine($"- [{m.CreatedAt:yyyy-MM-dd}] {content}{exp}");
            }
            sb.AppendLine();

            sb.AppendLine("## Projects & Project Memory");
            if (projects.Count == 0) sb.AppendLine("_No projects recorded._");
            foreach (var p in projects)
            {
                sb.AppendLine($"### {SecretRedactor.Redact(p.Name)}");
                if (!string.IsNullOrWhiteSpace(p.Description))
                {
                    sb.AppendLine(SecretRedactor.Redact(p.Description));
                }
                var pMem = projectMemories.Where(m => m.ProjectId == p.Id).ToList();
                if (pMem.Count > 0)
                {
                    sb.AppendLine("Notes:");
                    foreach (var m in pMem)
                    {
                        sb.AppendLine($"  - {SecretRedactor.Redact(m.Content)}");
                    }
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }
        else
        {
            var exportObj = new
            {
                export_date = DateTimeOffset.UtcNow,
                version = "1.0",
                long_term_memories = longTerm.Select(m => new
                {
                    m.Id,
                    Content = SecretRedactor.Redact(m.Content),
                    ContextExplanation = SecretRedactor.Redact(m.ContextExplanation),
                    m.CreatedAt,
                    m.IsPinned
                }),
                projects = projects.Select(p => new
                {
                    p.Id,
                    Name = SecretRedactor.Redact(p.Name),
                    Description = SecretRedactor.Redact(p.Description),
                    p.CreatedAt,
                    Notes = projectMemories.Where(m => m.ProjectId == p.Id).Select(m => new
                    {
                        m.Id,
                        Content = SecretRedactor.Redact(m.Content),
                        m.CreatedAt
                    })
                })
            };

            return JsonSerializer.Serialize(exportObj, new JsonSerializerOptions { WriteIndented = true });
        }
    }

    /// <summary>
    /// Exports the operational activity timeline separately from explicit memories.
    /// Automatically redacts sensitive secrets.
    /// </summary>
    public async Task<string> ExportTimelineAsync(string format = "json", CancellationToken cancellationToken = default)
    {
        var events = await _timelineRepository.GetRecentEventsAsync(500, cancellationToken);

        if (string.Equals(format, "markdown", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(format, "md", StringComparison.OrdinalIgnoreCase))
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Niki AI — Activity Timeline Event Log");
            sb.AppendLine($"Export Date: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine();

            foreach (var evt in events)
            {
                var summary = SecretRedactor.Redact(evt.Summary);
                sb.AppendLine($"- **{evt.Timestamp:yyyy-MM-dd HH:mm:ss}** [{evt.EventType}] ({evt.Source}): {summary}");
            }

            return sb.ToString();
        }
        else
        {
            var exportObj = new
            {
                export_date = DateTimeOffset.UtcNow,
                event_count = events.Count,
                events = events.Select(e => new
                {
                    e.Id,
                    e.EventType,
                    e.Source,
                    Summary = SecretRedactor.Redact(e.Summary),
                    DetailsJson = SecretRedactor.Redact(e.DetailsJson),
                    e.Timestamp,
                    e.RelatedId
                })
            };

            return JsonSerializer.Serialize(exportObj, new JsonSerializerOptions { WriteIndented = true });
        }
    }

    #endregion
}
