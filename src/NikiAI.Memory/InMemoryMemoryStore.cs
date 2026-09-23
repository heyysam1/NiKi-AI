using System.Collections.Concurrent;
using NikiAI.Core.Memory;

namespace NikiAI.Memory;

/// <summary>
/// In-memory implementation of IMemoryStore for testing and fallback environments.
/// </summary>
public class InMemoryMemoryStore : IMemoryStore
{
    private readonly ConcurrentDictionary<string, MemoryItem> _store = new();

    public Task SaveMemoryAsync(MemoryItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        _store[item.Id] = item;
        return Task.CompletedTask;
    }

    public Task<MemoryItem?> GetMemoryByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        _store.TryGetValue(id, out var item);
        return Task.FromResult(item);
    }

    public Task<IReadOnlyList<MemoryItem>> GetMemoriesAsync(MemoryCategory category, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var items = _store.Values
            .Where(m => m.Category == category && (m.ExpiresAt == null || m.ExpiresAt > now))
            .OrderByDescending(m => m.CreatedAt)
            .ToList();
        return Task.FromResult<IReadOnlyList<MemoryItem>>(items);
    }

    public Task<IReadOnlyList<MemoryItem>> SearchMemoriesAsync(string query, MemoryCategory? category = null, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var q = query.Trim();
        var items = _store.Values
            .Where(m => (category == null || m.Category == category) &&
                        (m.ExpiresAt == null || m.ExpiresAt > now) &&
                        (string.IsNullOrWhiteSpace(q) || m.Content.Contains(q, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(m => m.CreatedAt)
            .ToList();
        return Task.FromResult<IReadOnlyList<MemoryItem>>(items);
    }

    public Task<IReadOnlyList<MemoryItem>> GetProjectMemoriesAsync(string projectId, CancellationToken cancellationToken = default)
    {
        var items = _store.Values
            .Where(m => m.Category == MemoryCategory.Project && m.ProjectId == projectId)
            .OrderByDescending(m => m.CreatedAt)
            .ToList();
        return Task.FromResult<IReadOnlyList<MemoryItem>>(items);
    }

    public Task DeleteMemoryAsync(string id, CancellationToken cancellationToken = default)
    {
        _store.TryRemove(id, out _);
        return Task.CompletedTask;
    }

    public Task ClearCategoryAsync(MemoryCategory category, CancellationToken cancellationToken = default)
    {
        var keysToRemove = _store.Values.Where(m => m.Category == category).Select(m => m.Id).ToList();
        foreach (var key in keysToRemove)
        {
            _store.TryRemove(key, out _);
        }
        return Task.CompletedTask;
    }
}
