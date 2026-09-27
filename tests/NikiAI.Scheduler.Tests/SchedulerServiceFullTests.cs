using NikiAI.Core.Notifications;
using NikiAI.Core.Scheduler;
using NikiAI.Core.Tools;
using NikiAI.Scheduler;
using NikiAI.Storage;
using NikiAI.Tools;

namespace NikiAI.Scheduler.Tests;

public class SchedulerServiceFullTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly StorageContext _storageContext;
    private readonly SqliteScheduledItemRepository _repository;
    private DateTimeOffset _simulatedNow;

    public SchedulerServiceFullTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"niki_scheduler_test_{Guid.NewGuid():N}.db");
        _storageContext = new StorageContext(_testDbPath);
        _storageContext.InitializeAsync().GetAwaiter().GetResult();
        _repository = new SqliteScheduledItemRepository(_storageContext);
        _simulatedNow = new DateTimeOffset(2026, 9, 21, 10, 0, 0, TimeSpan.Zero);
    }

    [Fact]
    public async Task ScheduleOneTime_TriggersWhenDue_AndUpdatesStatus()
    {
        var triggeredItems = new List<ScheduledItem>();
        var service = new SchedulerService(_repository, null, () => _simulatedNow);
        service.ItemTriggered += item =>
        {
            triggeredItems.Add(item);
            return Task.CompletedTask;
        };

        var dueTime = _simulatedNow.AddMinutes(5);
        var item = new ScheduledItem(
            id: "one-time-1",
            title: "Doctor Appointment",
            scheduledTime: dueTime,
            isRecurring: false,
            description: "Annual medical checkup"
        );

        await service.ScheduleAsync(item);

        // Before due time: should not trigger
        await service.TriggerPendingDueItemsAsync(_simulatedNow);
        Assert.Empty(triggeredItems);

        // Advance simulated time past due
        _simulatedNow = dueTime.AddSeconds(1);
        await service.TriggerPendingDueItemsAsync(_simulatedNow);

        Assert.Single(triggeredItems);
        Assert.Equal("one-time-1", triggeredItems[0].Id);
        Assert.Equal(ScheduledItemStatus.Triggered, triggeredItems[0].Status);

        // Verify SQLite reflects status
        var inDb = await _repository.GetByIdAsync("one-time-1");
        Assert.NotNull(inDb);
        Assert.Equal(ScheduledItemStatus.Triggered, inDb.Status);
    }

    [Fact]
    public async Task ScheduleRecurring_AdvancesInterval_AndTriggersRepeatedly()
    {
        var triggeredCount = 0;
        var service = new SchedulerService(_repository, null, () => _simulatedNow);
        service.ItemTriggered += item =>
        {
            triggeredCount++;
            return Task.CompletedTask;
        };

        var firstDue = _simulatedNow.AddHours(1);
        var interval = TimeSpan.FromHours(2);
        var recurringItem = new ScheduledItem(
            id: "rec-1",
            title: "Hydration Check",
            scheduledTime: firstDue,
            isRecurring: true,
            recurrenceInterval: interval,
            itemType: ScheduledItemType.RecurringTask
        );

        await service.ScheduleAsync(recurringItem);

        // First trigger
        _simulatedNow = firstDue.AddSeconds(1);
        await service.TriggerPendingDueItemsAsync(_simulatedNow);

        Assert.Equal(1, triggeredCount);
        var storedAfter1 = await _repository.GetByIdAsync("rec-1");
        Assert.NotNull(storedAfter1);
        Assert.Equal(ScheduledItemStatus.Scheduled, storedAfter1.Status);
        Assert.Equal(firstDue.Add(interval), storedAfter1.ScheduledTime);

        // Second trigger
        _simulatedNow = firstDue.Add(interval).AddSeconds(1);
        await service.TriggerPendingDueItemsAsync(_simulatedNow);

        Assert.Equal(2, triggeredCount);
        var storedAfter2 = await _repository.GetByIdAsync("rec-1");
        Assert.NotNull(storedAfter2);
        Assert.Equal(firstDue.Add(interval).Add(interval), storedAfter2.ScheduledTime);
    }

    [Fact]
    public async Task RelativeDelayReminder_ViaCreateReminderTool_SchedulesInRepository()
    {
        var service = new SchedulerService(_repository, null, () => _simulatedNow);
        var tool = new CreateReminderTool(() => _simulatedNow, service);

        var call = new ToolCall("c_rem_rel", "create_reminder", """{"message": "Stretch legs", "delay_seconds": 300, "title": "Break"}""", _simulatedNow);
        var result = await tool.ExecuteAsync(call);

        Assert.True(result.IsSuccess);

        var pending = await service.GetPendingItemsAsync();
        Assert.Single(pending);
        var scheduled = pending[0];
        Assert.Equal("Break", scheduled.Title);
        Assert.Equal("Stretch legs", scheduled.Description);
        Assert.Equal(_simulatedNow.AddSeconds(300), scheduled.ScheduledTime);

        // Check SQLite
        var inDb = await _repository.GetByIdAsync(scheduled.Id);
        Assert.NotNull(inDb);
        Assert.Equal("Stretch legs", inDb.Description);
    }

    [Fact]
    public async Task Snooze_AdvancesScheduledTime_AndSetsStatusSnoozed()
    {
        var service = new SchedulerService(_repository, null, () => _simulatedNow);
        var item = new ScheduledItem(
            id: "snooze-test",
            title: "Task Review",
            scheduledTime: _simulatedNow.AddMinutes(5),
            isRecurring: false
        );
        await service.ScheduleAsync(item);

        // Snooze for 15 minutes
        await service.SnoozeAsync("snooze-test", TimeSpan.FromMinutes(15));

        var snoozed = await service.GetItemByIdAsync("snooze-test");
        Assert.NotNull(snoozed);
        Assert.Equal(ScheduledItemStatus.Snoozed, snoozed.Status);
        Assert.Equal(_simulatedNow.AddMinutes(15), snoozed.ScheduledTime);

        var inDb = await _repository.GetByIdAsync("snooze-test");
        Assert.NotNull(inDb);
        Assert.Equal(ScheduledItemStatus.Snoozed, inDb.Status);
    }

    [Fact]
    public async Task Dismiss_And_Cancel_TransitionStatus_AndRemoveFromActive()
    {
        var service = new SchedulerService(_repository, null, () => _simulatedNow);
        var item1 = new ScheduledItem(id: "d-1", title: "Item 1", scheduledTime: _simulatedNow.AddMinutes(10));
        var item2 = new ScheduledItem(id: "d-2", title: "Item 2", scheduledTime: _simulatedNow.AddMinutes(20));

        await service.ScheduleAsync(item1);
        await service.ScheduleAsync(item2);

        await service.DismissAsync("d-1");
        await service.CancelAsync("d-2");

        var pending = await service.GetPendingItemsAsync();
        Assert.Empty(pending);

        var inDb1 = await _repository.GetByIdAsync("d-1");
        var inDb2 = await _repository.GetByIdAsync("d-2");

        Assert.NotNull(inDb1);
        Assert.Equal(ScheduledItemStatus.Dismissed, inDb1.Status);

        Assert.NotNull(inDb2);
        Assert.Equal(ScheduledItemStatus.Cancelled, inDb2.Status);
    }

    [Fact]
    public async Task MissedSchedule_EvaluatedAndTriggeredOnStartup()
    {
        // Add an item that was due 2 hours in the past
        var pastTime = _simulatedNow.AddHours(-2);
        var missedItem = new ScheduledItem(
            id: "missed-1",
            title: "Offline Missed Reminder",
            scheduledTime: pastTime,
            isRecurring: false,
            description: "Was offline",
            status: ScheduledItemStatus.Scheduled
        );
        await _repository.CreateOrUpdateAsync(missedItem);

        // Cold start scheduler
        var triggered = new List<ScheduledItem>();
        var coldService = new SchedulerService(_repository, null, () => _simulatedNow);
        coldService.ItemTriggered += item =>
        {
            triggered.Add(item);
            return Task.CompletedTask;
        };

        await coldService.StartAsync();
        await Task.Delay(50);
        await coldService.StopAsync();

        Assert.Single(triggered);
        Assert.Equal("missed-1", triggered[0].Id);
        Assert.Equal(ScheduledItemStatus.Missed, triggered[0].Status);

        var updatedInDb = await _repository.GetByIdAsync("missed-1");
        Assert.NotNull(updatedInDb);
        Assert.Equal(ScheduledItemStatus.Missed, updatedInDb.Status);
    }

    [Fact]
    public async Task RestartPersistence_SurvivesRepositoryColdStart()
    {
        var service1 = new SchedulerService(_repository, null, () => _simulatedNow);
        var item = new ScheduledItem(
            id: "persist-1",
            title: "Persistent Reminder",
            scheduledTime: _simulatedNow.AddHours(5),
            description: "Survives app termination"
        );
        await service1.ScheduleAsync(item);

        // Simulate shutdown of service1
        service1.Dispose();

        // Simulate new app session loading same SQLite DB
        var service2 = new SchedulerService(_repository, null, () => _simulatedNow);
        await service2.StartAsync();

        var pending = await service2.GetPendingItemsAsync();
        await service2.StopAsync();

        Assert.Single(pending);
        Assert.Equal("persist-1", pending[0].Id);
        Assert.Equal("Persistent Reminder", pending[0].Title);
        Assert.Equal(_simulatedNow.AddHours(5), pending[0].ScheduledTime);
    }

    [Fact]
    public async Task UtcConsistency_HandlesDifferentTimezonesAccurately()
    {
        var service = new SchedulerService(_repository, null, () => _simulatedNow);

        // Scheduled in +06:00 offset
        var offsetTime = new DateTimeOffset(2026, 9, 21, 16, 0, 0, TimeSpan.FromHours(6)); // Same moment as 10:00 UTC
        var item = new ScheduledItem(
            id: "tz-1",
            title: "Timezone Test",
            scheduledTime: offsetTime
        );

        await service.ScheduleAsync(item);

        // Check UTC equality
        var stored = await _repository.GetByIdAsync("tz-1");
        Assert.NotNull(stored);
        Assert.Equal(_simulatedNow.ToUniversalTime(), stored.ScheduledTime.ToUniversalTime());
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_testDbPath))
            {
                File.Delete(_testDbPath);
            }
        }
        catch { }
    }
}
