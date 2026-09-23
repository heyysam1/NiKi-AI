namespace NikiAI.Core.Memory;

/// <summary>
/// Memory categories as defined in 02_FEATURES_AND_SCOPE.md and 04_ARCHITECTURE.md.
/// </summary>
public enum MemoryCategory
{
    LongTerm,
    Project
}

/// <summary>
/// Represents a discrete memory unit in the Niki AI Memory Subsystem.
/// </summary>
public record MemoryItem(
    string Id,
    MemoryCategory Category,
    string Content,
    DateTimeOffset CreatedAt,
    string? ContextExplanation = null,
    DateTimeOffset? UpdatedAt = null,
    string? ProjectId = null,
    string? MetadataJson = null,
    bool IsPinned = false,
    DateTimeOffset? ExpiresAt = null
);

/// <summary>
/// Memory store contract for local persistence of explicit knowledge and user preferences.
/// </summary>
public interface IMemoryStore
{
    Task SaveMemoryAsync(MemoryItem item, CancellationToken cancellationToken = default);
    Task<MemoryItem?> GetMemoryByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MemoryItem>> GetMemoriesAsync(MemoryCategory category, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MemoryItem>> SearchMemoriesAsync(string query, MemoryCategory? category = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MemoryItem>> GetProjectMemoriesAsync(string projectId, CancellationToken cancellationToken = default);
    Task DeleteMemoryAsync(string id, CancellationToken cancellationToken = default);
    Task ClearCategoryAsync(MemoryCategory category, CancellationToken cancellationToken = default);
}
