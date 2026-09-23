namespace NikiAI.Core.Memory;

/// <summary>
/// Core contract for memory management, explicit knowledge operations, project scoping, and privacy controls.
/// </summary>
public interface IMemoryService
{
    bool IsMemoryEnabled();
    Task SetMemoryEnabledAsync(bool enabled, CancellationToken cancellationToken = default);

    Task<MemoryItem> SaveExplicitMemoryAsync(
        string content,
        MemoryCategory category = MemoryCategory.LongTerm,
        string? contextExplanation = null,
        string? projectId = null,
        bool isPinned = false,
        DateTimeOffset? expiresAt = null,
        CancellationToken cancellationToken = default);

    Task<MemoryItem?> GetMemoryByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MemoryItem>> GetMemoriesAsync(MemoryCategory category, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MemoryItem>> GetAllMemoriesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MemoryItem>> SearchMemoriesAsync(string query, MemoryCategory? category = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MemoryItem>> GetProjectMemoriesAsync(string projectId, CancellationToken cancellationToken = default);
    Task DeleteMemoryAsync(string id, CancellationToken cancellationToken = default);
    Task ClearCategoryAsync(MemoryCategory category, CancellationToken cancellationToken = default);

    Task<Project> CreateProjectAsync(string name, string? description = null, CancellationToken cancellationToken = default);
    Task<Project?> GetProjectByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Project>> GetAllProjectsAsync(CancellationToken cancellationToken = default);
    Task<bool> DeleteProjectAsync(string id, CancellationToken cancellationToken = default);

    Task LogTimelineEventAsync(string eventType, string source, string summary, string? detailsJson = null, string? relatedId = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TimelineEvent>> GetRecentTimelineEventsAsync(int limit = 50, CancellationToken cancellationToken = default);
    Task<int> ClearAllTimelineEventsAsync(CancellationToken cancellationToken = default);

    Task<string> ExportMemoriesAsync(string format = "json", CancellationToken cancellationToken = default);
    Task<string> ExportTimelineAsync(string format = "json", CancellationToken cancellationToken = default);
}
