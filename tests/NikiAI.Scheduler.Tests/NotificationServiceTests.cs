using NikiAI.Core.Logging;
using NikiAI.Core.Notifications;
using NikiAI.Notifications;
using NikiAI.Storage;

namespace NikiAI.Scheduler.Tests;

public class NotificationServiceTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly StorageContext _storageContext;
    private readonly SqliteNotificationRepository _repository;
    private DateTimeOffset _simulatedNow;

    public NotificationServiceTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"niki_notif_test_{Guid.NewGuid():N}.db");
        _storageContext = new StorageContext(_testDbPath);
        _storageContext.InitializeAsync().GetAwaiter().GetResult();
        _repository = new SqliteNotificationRepository(_storageContext);
        _simulatedNow = new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);
    }

    [Fact]
    public async Task ShowNotification_DispatchesToNativeAndPopupHandlers()
    {
        NotificationPayload? nativeReceived = null;
        NotificationPayload? popupReceived = null;

        var nativeHandler = new TestNativeHandler(p => nativeReceived = p);
        var popupHandler = new TestPopupHandler(p => popupReceived = p);

        var service = new NotificationService(_repository, nativeHandler, popupHandler, () => _simulatedNow);

        var payload = new NotificationPayload(
            notificationId: "notif-1",
            title: "Task Done",
            summaryMessage: "All operations succeeded",
            showPopup: true
        );

        await service.ShowNotificationAsync(payload);

        Assert.NotNull(nativeReceived);
        Assert.Equal("notif-1", nativeReceived.NotificationId);
        Assert.Equal("Task Done", nativeReceived.Title);

        Assert.NotNull(popupReceived);
        Assert.Equal("notif-1", popupReceived.NotificationId);
    }

    [Fact]
    public async Task ShowNotification_PersistsToSQLiteNotificationHistory()
    {
        var service = new NotificationService(_repository, null, null, () => _simulatedNow);

        var payload = new NotificationPayload(
            notificationId: "history-test-1",
            title: "File Saved",
            summaryMessage: "Report saved to D:\\report.pdf",
            associatedTaskId: "task-99",
            showPopup: true,
            keyOutputs: new List<string> { "Output 1", "Output 2" }
        );

        await service.ShowNotificationAsync(payload);

        var history = await service.GetHistoryAsync(10);
        Assert.Single(history);
        Assert.Equal("history-test-1", history[0].NotificationId);
        Assert.Equal("File Saved", history[0].Title);
        Assert.Equal("task-99", history[0].AssociatedTaskId);
        Assert.NotNull(history[0].KeyOutputs);
        Assert.Equal(2, history[0].KeyOutputs!.Count);
    }

    [Fact]
    public async Task ShowNotification_RedactsSecrets_FromTitleSummaryAndOutputs()
    {
        var service = new NotificationService(_repository, null, null, () => _simulatedNow);

        var secretApiKey = "sk-live12345678901234567890";
        var secretAiza = "AIzaSyD987654321012345678901234567890";

        var payload = new NotificationPayload(
            notificationId: "sec-1",
            title: $"Connected with {secretApiKey}",
            summaryMessage: $"Auth token {secretAiza} accepted",
            keyOutputs: new List<string> { $"Header: Bearer {secretApiKey}" }
        );

        await service.ShowNotificationAsync(payload);

        var history = await service.GetHistoryAsync(10);
        Assert.Single(history);
        var stored = history[0];

        Assert.DoesNotContain(secretApiKey, stored.Title);
        Assert.DoesNotContain(secretAiza, stored.SummaryMessage);
        Assert.DoesNotContain(secretApiKey, stored.KeyOutputs![0]);

        Assert.Contains(SecretRedactor.RedactedMask, stored.Title);
        Assert.Contains(SecretRedactor.RedactedMask, stored.SummaryMessage);
        Assert.Contains(SecretRedactor.RedactedMask, stored.KeyOutputs![0]);
    }

    [Fact]
    public async Task ShowNotification_DeduplicatesRapidIdenticalNotifications()
    {
        var dispatched = new List<NotificationPayload>();
        var nativeHandler = new TestNativeHandler(p => dispatched.Add(p));
        var service = new NotificationService(_repository, nativeHandler, null, () => _simulatedNow);

        var payload1 = new NotificationPayload("n1", "Reminder", "Stand up", "task-1");
        var payload2 = new NotificationPayload("n2", "Reminder", "Stand up", "task-1");

        // Within deduplication window (< 2 seconds)
        await service.ShowNotificationAsync(payload1);
        _simulatedNow = _simulatedNow.AddMilliseconds(500);
        await service.ShowNotificationAsync(payload2);

        Assert.Single(dispatched);

        // Advance past deduplication window (3 seconds later)
        _simulatedNow = _simulatedNow.AddSeconds(3);
        var payload3 = new NotificationPayload("n3", "Reminder", "Stand up", "task-1");
        await service.ShowNotificationAsync(payload3);

        Assert.Equal(2, dispatched.Count);
    }

    [Fact]
    public async Task ClearHistory_RemovesStoredNotifications()
    {
        var service = new NotificationService(_repository, null, null, () => _simulatedNow);

        await service.ShowNotificationAsync(new NotificationPayload("c1", "T1", "M1"));
        await service.ShowNotificationAsync(new NotificationPayload("c2", "T2", "M2"));

        var beforeClear = await service.GetHistoryAsync(10);
        Assert.Equal(2, beforeClear.Count);

        await service.ClearHistoryAsync();

        var afterClear = await service.GetHistoryAsync(10);
        Assert.Empty(afterClear);
    }

    [Fact]
    public async Task NotificationReceived_EventFiresWithSanitizedPayload()
    {
        NotificationPayload? eventPayload = null;
        var service = new NotificationService(_repository, null, null, () => _simulatedNow);
        service.NotificationReceived += p =>
        {
            eventPayload = p;
            return Task.CompletedTask;
        };

        var rawSecret = "sk-mySecretKey1234567890abc";
        await service.ShowNotificationAsync(new NotificationPayload("evt-1", $"Alert with {rawSecret}", "Summary text"));

        Assert.NotNull(eventPayload);
        Assert.Equal("evt-1", eventPayload.NotificationId);
        Assert.DoesNotContain(rawSecret, eventPayload.Title);
        Assert.Contains(SecretRedactor.RedactedMask, eventPayload.Title);
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

    private class TestNativeHandler : INativeNotificationHandler
    {
        private readonly Action<NotificationPayload> _onShow;
        public TestNativeHandler(Action<NotificationPayload> onShow) => _onShow = onShow;
        public Task ShowNativeNotificationAsync(NotificationPayload payload, CancellationToken cancellationToken = default)
        {
            _onShow(payload);
            return Task.CompletedTask;
        }
    }

    private class TestPopupHandler : IResultPopupHandler
    {
        private readonly Action<NotificationPayload> _onShow;
        public TestPopupHandler(Action<NotificationPayload> onShow) => _onShow = onShow;
        public Task ShowResultPopupAsync(NotificationPayload payload, CancellationToken cancellationToken = default)
        {
            _onShow(payload);
            return Task.CompletedTask;
        }
    }
}
