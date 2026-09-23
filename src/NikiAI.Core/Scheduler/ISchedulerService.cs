namespace NikiAI.Core.Scheduler;

public enum ScheduledItemType
{
    Reminder = 0,
    RecurringTask = 1,
    TaskDeadline = 2
}

public enum ScheduledItemStatus
{
    Scheduled = 0,
    Triggered = 1,
    Completed = 2,
    Cancelled = 3,
    Snoozed = 4,
    Missed = 5,
    Dismissed = 6
}

public record ScheduledItem
{
    public string Id { get; init; }
    public string Title { get; init; }
    public DateTimeOffset ScheduledTime { get; init; }
    public bool IsRecurring { get; init; }
    public TimeSpan? RecurrenceInterval { get; init; }
    public string? AssociatedTaskId { get; init; }
    public string? Description { get; init; }
    public ScheduledItemType ItemType { get; init; }
    public ScheduledItemStatus Status { get; init; }
    public string? PayloadJson { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }

    public ScheduledItem(
        string id,
        string title,
        DateTimeOffset scheduledTime,
        bool isRecurring = false,
        TimeSpan? recurrenceInterval = null,
        string? associatedTaskId = null,
        string? description = null,
        ScheduledItemType itemType = ScheduledItemType.Reminder,
        ScheduledItemStatus status = ScheduledItemStatus.Scheduled,
        string? payloadJson = null,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? updatedAt = null)
    {
        Id = id;
        Title = title;
        ScheduledTime = scheduledTime;
        IsRecurring = isRecurring;
        RecurrenceInterval = recurrenceInterval;
        AssociatedTaskId = associatedTaskId;
        Description = description;
        ItemType = itemType;
        Status = status;
        PayloadJson = payloadJson;
        CreatedAt = createdAt ?? DateTimeOffset.UtcNow;
        UpdatedAt = updatedAt ?? DateTimeOffset.UtcNow;
    }

    // Overload preserving exact signature and parameter names from Phase 0 tests
    public ScheduledItem(
        string Id,
        string Title,
        DateTimeOffset ScheduledTime,
        bool IsRecurring,
        TimeSpan? RecurrenceInterval,
        string? AssociatedTaskId)
        : this(Id, Title, ScheduledTime, IsRecurring, RecurrenceInterval, AssociatedTaskId, null, ScheduledItemType.Reminder, ScheduledItemStatus.Scheduled, null, null, null)
    {
    }
}

/// <summary>
/// Central scheduler service contract.
/// </summary>
public interface ISchedulerService
{
    Task ScheduleAsync(ScheduledItem item, CancellationToken cancellationToken = default);
    Task CancelAsync(string id, CancellationToken cancellationToken = default);
    Task SnoozeAsync(string id, TimeSpan duration, CancellationToken cancellationToken = default);
    Task DismissAsync(string id, CancellationToken cancellationToken = default);
    Task<ScheduledItem?> GetItemByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ScheduledItem>> GetPendingItemsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ScheduledItem>> GetAllItemsAsync(CancellationToken cancellationToken = default);
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    event Func<ScheduledItem, Task>? ItemTriggered;
}
