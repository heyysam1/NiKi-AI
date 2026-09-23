using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using NikiAI.Core.Notifications;
using NikiAI.Core.Scheduler;

namespace NikiAI.Scheduler;

/// <summary>
/// Production central scheduler service.
/// Manages one-time reminders, recurring tasks, snooze, dismiss, and restart persistence.
/// Uses an event-driven background loop avoiding tight polling.
/// </summary>
public class SchedulerService : ISchedulerService, IDisposable
{
    private readonly IScheduledItemRepository? _repository;
    private readonly INotificationService? _notificationService;
    private readonly Func<DateTimeOffset> _timeProvider;
    private readonly ILogger<SchedulerService>? _logger;

    private readonly ConcurrentDictionary<string, ScheduledItem> _activeItems = new();
    private readonly SemaphoreSlim _signal = new(0, 1);
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private bool _isDisposed;

    public event Func<ScheduledItem, Task>? ItemTriggered;

    public SchedulerService(
        IScheduledItemRepository? repository = null,
        INotificationService? notificationService = null,
        Func<DateTimeOffset>? timeProvider = null,
        ILogger<SchedulerService>? logger = null)
    {
        _repository = repository;
        _notificationService = notificationService;
        _timeProvider = timeProvider ?? (() => DateTimeOffset.UtcNow);
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_loopTask != null) return;

        _logger?.LogInformation("Starting SchedulerService background loop...");
        _loopCts = new CancellationTokenSource();

        // 1. Recover pending items from persistent repository (missed schedule handling)
        if (_repository != null)
        {
            var now = _timeProvider();
            var persisted = await _repository.GetAllAsync(cancellationToken);
            foreach (var item in persisted)
            {
                if (item.Status is ScheduledItemStatus.Scheduled or ScheduledItemStatus.Snoozed)
                {
                    _activeItems[item.Id] = item;
                }
            }

            _logger?.LogInformation("Loaded {Count} active scheduled items from storage.", _activeItems.Count);

            // Handle any missed schedules (items due in the past while application was closed)
            await EvaluateDueItemsInternalAsync(now, cancellationToken);
        }

        // 2. Start background worker loop
        _loopTask = Task.Run(() => WorkerLoopAsync(_loopCts.Token), CancellationToken.None);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_loopCts == null) return;

        _logger?.LogInformation("Stopping SchedulerService background loop...");
        _loopCts.Cancel();
        WakeUpLoop();

        if (_loopTask != null)
        {
            try
            {
                var completed = await Task.WhenAny(_loopTask, Task.Delay(2000, cancellationToken));
                if (completed != _loopTask)
                {
                    _logger?.LogWarning("SchedulerService background loop did not exit within timeout during StopAsync.");
                }
            }
            catch (OperationCanceledException) { }
            _loopTask = null;
        }

        _loopCts.Dispose();
        _loopCts = null;
    }

    public async Task ScheduleAsync(ScheduledItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        _activeItems[item.Id] = item;

        if (_repository != null)
        {
            await _repository.CreateOrUpdateAsync(item, cancellationToken);
        }

        _logger?.LogInformation("Scheduled item {Id} ('{Title}') for {ScheduledTime} (Recurring: {IsRecurring})",
            item.Id, item.Title, item.ScheduledTime, item.IsRecurring);

        WakeUpLoop();
    }

    public async Task CancelAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        if (_activeItems.TryRemove(id, out var item))
        {
            var cancelled = item with { Status = ScheduledItemStatus.Cancelled, UpdatedAt = _timeProvider() };
            if (_repository != null)
            {
                await _repository.UpdateStatusAsync(id, ScheduledItemStatus.Cancelled, cancellationToken: cancellationToken);
            }
            _logger?.LogInformation("Cancelled scheduled item {Id} ('{Title}')", id, item.Title);
            WakeUpLoop();
        }
        else if (_repository != null)
        {
            await _repository.UpdateStatusAsync(id, ScheduledItemStatus.Cancelled, cancellationToken: cancellationToken);
        }
    }

    public async Task SnoozeAsync(string id, TimeSpan duration, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), "Snooze duration must be positive.");
        }

        var now = _timeProvider();
        ScheduledItem? current = null;

        if (_activeItems.TryGetValue(id, out var inMemory))
        {
            current = inMemory;
        }
        else if (_repository != null)
        {
            current = await _repository.GetByIdAsync(id, cancellationToken);
        }

        if (current == null)
        {
            throw new KeyNotFoundException($"Scheduled item '{id}' was not found to snooze.");
        }

        var newDue = now.Add(duration);
        var snoozed = current with
        {
            ScheduledTime = newDue,
            Status = ScheduledItemStatus.Snoozed,
            UpdatedAt = now
        };

        _activeItems[id] = snoozed;

        if (_repository != null)
        {
            await _repository.UpdateStatusAsync(id, ScheduledItemStatus.Snoozed, nextTime: newDue, cancellationToken: cancellationToken);
        }

        _logger?.LogInformation("Snoozed scheduled item {Id} ('{Title}') for {Duration}; new due time: {NewDue}",
            id, snoozed.Title, duration, newDue);

        WakeUpLoop();
    }

    public async Task DismissAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        _activeItems.TryRemove(id, out var item);

        if (_repository != null)
        {
            await _repository.UpdateStatusAsync(id, ScheduledItemStatus.Dismissed, cancellationToken: cancellationToken);
        }

        _logger?.LogInformation("Dismissed scheduled item {Id}", id);
        WakeUpLoop();
    }

    public async Task<ScheduledItem?> GetItemByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        if (_activeItems.TryGetValue(id, out var item))
        {
            return item;
        }

        if (_repository != null)
        {
            return await _repository.GetByIdAsync(id, cancellationToken);
        }

        return null;
    }

    public Task<IReadOnlyList<ScheduledItem>> GetPendingItemsAsync(CancellationToken cancellationToken = default)
    {
        var pending = _activeItems.Values
            .Where(i => i.Status is ScheduledItemStatus.Scheduled or ScheduledItemStatus.Snoozed)
            .OrderBy(i => i.ScheduledTime)
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyList<ScheduledItem>>(pending);
    }

    public async Task<IReadOnlyList<ScheduledItem>> GetAllItemsAsync(CancellationToken cancellationToken = default)
    {
        if (_repository != null)
        {
            return await _repository.GetAllAsync(cancellationToken);
        }

        return _activeItems.Values.OrderBy(i => i.ScheduledTime).ToList().AsReadOnly();
    }

    /// <summary>
    /// Explicit evaluation method allowing unit tests and runtime checks to trigger due items deterministically.
    /// </summary>
    public Task TriggerPendingDueItemsAsync(DateTimeOffset asOfTime, CancellationToken cancellationToken = default)
    {
        return EvaluateDueItemsInternalAsync(asOfTime, cancellationToken);
    }

    private async Task WorkerLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var now = _timeProvider();
                await EvaluateDueItemsInternalAsync(now, ct);

                // Calculate next wake-up delay based on earliest pending item
                var nextDue = GetEarliestPendingDueTime();
                TimeSpan delay;

                if (!nextDue.HasValue)
                {
                    // No pending items: sleep until a new item is scheduled or 60 seconds
                    delay = TimeSpan.FromSeconds(60);
                }
                else
                {
                    var delta = nextDue.Value - _timeProvider();
                    if (delta <= TimeSpan.Zero)
                    {
                        delay = TimeSpan.Zero;
                    }
                    else if (delta > TimeSpan.FromSeconds(60))
                    {
                        delay = TimeSpan.FromSeconds(60);
                    }
                    else
                    {
                        delay = delta;
                    }
                }

                if (delay > TimeSpan.Zero)
                {
                    // Wait for delay OR until woken up early by Schedule/Snooze/Cancel
                    await _signal.WaitAsync(delay, ct);
                }
                else
                {
                    // Due immediately; brief yield
                    await Task.Yield();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error occurred in SchedulerService worker loop.");
                try
                {
                    await Task.Delay(1000, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task EvaluateDueItemsInternalAsync(DateTimeOffset now, CancellationToken ct)
    {
        var dueItems = _activeItems.Values
            .Where(i => i.Status is ScheduledItemStatus.Scheduled or ScheduledItemStatus.Snoozed && i.ScheduledTime <= now)
            .OrderBy(i => i.ScheduledTime)
            .ToList();

        foreach (var item in dueItems)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                if (item.IsRecurring && item.RecurrenceInterval.HasValue && item.RecurrenceInterval.Value > TimeSpan.Zero)
                {
                    // Recurring Task: calculate next occurrence and update
                    var nextDue = item.ScheduledTime.Add(item.RecurrenceInterval.Value);
                    while (nextDue <= now)
                    {
                        nextDue = nextDue.Add(item.RecurrenceInterval.Value);
                    }

                    var updatedRecurring = item with
                    {
                        ScheduledTime = nextDue,
                        Status = ScheduledItemStatus.Scheduled,
                        UpdatedAt = now
                    };

                    _activeItems[item.Id] = updatedRecurring;

                    if (_repository != null)
                    {
                        await _repository.UpdateStatusAsync(item.Id, ScheduledItemStatus.Scheduled, nextTime: nextDue, cancellationToken: ct);
                    }

                    _logger?.LogInformation("Recurring item {Id} ('{Title}') triggered. Next occurrence scheduled for {NextDue}",
                        item.Id, item.Title, nextDue);

                    await TriggerItemAsync(item, ct);
                }
                else
                {
                    // One-time item: mark completed/triggered and remove from active tracking
                    var isPastMissed = (now - item.ScheduledTime) > TimeSpan.FromMinutes(5);
                    var finalStatus = isPastMissed ? ScheduledItemStatus.Missed : ScheduledItemStatus.Triggered;

                    var completed = item with
                    {
                        Status = finalStatus,
                        UpdatedAt = now
                    };

                    _activeItems.TryRemove(item.Id, out _);

                    if (_repository != null)
                    {
                        await _repository.UpdateStatusAsync(item.Id, finalStatus, cancellationToken: ct);
                    }

                    _logger?.LogInformation("One-time scheduled item {Id} ('{Title}') triggered with status {Status}.",
                        item.Id, item.Title, finalStatus);

                    await TriggerItemAsync(completed, ct);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to trigger scheduled item {Id} ('{Title}')", item.Id, item.Title);
            }
        }
    }

    private async Task TriggerItemAsync(ScheduledItem item, CancellationToken ct)
    {
        // 1. Emit ItemTriggered event
        if (ItemTriggered != null)
        {
            try
            {
                await ItemTriggered.Invoke(item);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in ItemTriggered event subscriber for item {Id}", item.Id);
            }
        }

        // 2. Dispatch notification via INotificationService
        if (_notificationService != null)
        {
            var notifType = item.ItemType == ScheduledItemType.Reminder ? NotificationType.Reminder : NotificationType.SystemAlert;
            var compState = item.Status == ScheduledItemStatus.Missed ? TaskCompletionState.Information : TaskCompletionState.Reminder;

            var payload = new NotificationPayload(
                notificationId: Guid.NewGuid().ToString("N"),
                title: item.Title,
                summaryMessage: item.Description ?? item.Title,
                associatedTaskId: item.AssociatedTaskId,
                showPopup: true,
                type: notifType,
                completionState: compState,
                oneSentenceSummary: item.Description ?? $"Reminder: {item.Title}",
                keyOutputs: new List<string> { $"Scheduled Time: {item.ScheduledTime:yyyy-MM-dd HH:mm:ss}", $"Status: {item.Status}" },
                artifactLinks: null,
                characterAvatarPath: null,
                createdAt: _timeProvider()
            );

            try
            {
                await _notificationService.ShowNotificationAsync(payload, ct);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to dispatch notification for triggered item {Id}", item.Id);
            }
        }
    }

    private DateTimeOffset? GetEarliestPendingDueTime()
    {
        var pending = _activeItems.Values
            .Where(i => i.Status is ScheduledItemStatus.Scheduled or ScheduledItemStatus.Snoozed)
            .ToList();

        if (pending.Count == 0) return null;
        return pending.Min(i => i.ScheduledTime);
    }

    private void WakeUpLoop()
    {
        if (_signal.CurrentCount == 0)
        {
            try
            {
                _signal.Release();
            }
            catch (SemaphoreFullException) { }
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _loopCts?.Cancel();
        _loopCts?.Dispose();
        _signal.Dispose();
    }
}
