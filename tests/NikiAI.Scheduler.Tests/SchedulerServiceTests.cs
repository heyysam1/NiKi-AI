using NikiAI.Core.Scheduler;
using NikiAI.Scheduler;

namespace NikiAI.Scheduler.Tests;

public class SchedulerServiceTests
{
    [Fact]
    public async Task ScheduleAsync_And_CancelAsync_WorkCorrectly()
    {
        var service = new SchedulerService();
        var item = new ScheduledItem(
            Id: "item-1",
            Title: "Break Reminder",
            ScheduledTime: DateTimeOffset.UtcNow.AddMinutes(30),
            IsRecurring: false,
            RecurrenceInterval: null,
            AssociatedTaskId: null
        );

        await service.ScheduleAsync(item);
        var pending = await service.GetPendingItemsAsync();

        Assert.Single(pending);
        Assert.Equal("item-1", pending[0].Id);
        Assert.Equal("Break Reminder", pending[0].Title);

        await service.CancelAsync("item-1");
        var afterCancel = await service.GetPendingItemsAsync();

        Assert.Empty(afterCancel);
    }
}
