namespace NikiAI.Core.Scheduler;

/// <summary>
/// Contract for persistent storage of scheduled items.
/// </summary>
public interface IScheduledItemRepository
{
    Task<ScheduledItem> CreateOrUpdateAsync(ScheduledItem item, CancellationToken cancellationToken = default);
    Task<ScheduledItem?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ScheduledItem>> GetPendingDueItemsAsync(DateTimeOffset asOfTime, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ScheduledItem>> GetAllAsync(CancellationToken cancellationToken = default);
    Task UpdateStatusAsync(string id, ScheduledItemStatus status, DateTimeOffset? nextTime = null, CancellationToken cancellationToken = default);
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
}
