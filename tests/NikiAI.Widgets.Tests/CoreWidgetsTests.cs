using NikiAI.Core.Agent;
using NikiAI.Core.Notifications;
using NikiAI.Core.Scheduler;
using NikiAI.Core.Tasks;
using NikiAI.Core.Tools;
using NikiAI.Core.Widgets;
using NikiAI.Widgets.Services;
using Xunit;

namespace NikiAI.Widgets.Tests;

public class CoreWidgetsTests
{
    #region Test Fakes

    private class FakeScheduledItemRepository : IScheduledItemRepository
    {
        public List<ScheduledItem> Items { get; } = new();

        public Task<ScheduledItem> CreateOrUpdateAsync(ScheduledItem item, CancellationToken cancellationToken = default)
        {
            Items.RemoveAll(i => i.Id == item.Id);
            Items.Add(item);
            return Task.FromResult(item);
        }

        public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
        {
            Items.RemoveAll(i => i.Id == id);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ScheduledItem>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ScheduledItem>>(Items.AsReadOnly());

        public Task<ScheduledItem?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.FirstOrDefault(i => i.Id == id));

        public Task<IReadOnlyList<ScheduledItem>> GetPendingDueItemsAsync(DateTimeOffset asOfTime, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ScheduledItem>>(Items.Where(i => i.ScheduledTime <= asOfTime).ToList().AsReadOnly());

        public Task UpdateStatusAsync(string id, ScheduledItemStatus status, DateTimeOffset? nextTime = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private class FakeTaskRepository : ITaskRepository
    {
        public List<AgentTask> Tasks { get; } = new();

        public Task<AgentTask> CreateAsync(AgentTask task, CancellationToken cancellationToken = default)
        {
            Tasks.Add(task);
            return Task.FromResult(task);
        }

        public Task<AgentTask?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Tasks.FirstOrDefault(t => t.Id == id));

        public Task<IReadOnlyList<AgentTask>> GetAllAsync(bool includeArchived = false, int limit = 100, int offset = 0, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AgentTask>>(Tasks.AsReadOnly());

        public Task<IReadOnlyList<AgentTask>> GetByStatusAsync(AgentTaskStatus status, bool includeArchived = false, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AgentTask>>(Tasks.Where(t => t.Status == status).ToList().AsReadOnly());

        public Task UpdateAsync(AgentTask task, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task TransitionStatusAsync(string taskId, AgentTaskStatus newStatus, string? message = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ArchiveAsync(string taskId, string? reason = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task PurgePermanentlyAsync(string taskId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task AddEventAsync(TaskEvent taskEvent, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<TaskEvent>> GetEventsAsync(string taskId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TaskEvent>>(Array.Empty<TaskEvent>());
    }

    private class FakeClipboardService : IClipboardService
    {
        public string? Content { get; set; }
        public Task<string?> GetTextAsync(CancellationToken cancellationToken = default) => Task.FromResult(Content);
        public Task SetTextAsync(string text, CancellationToken cancellationToken = default)
        {
            Content = text;
            return Task.CompletedTask;
        }
    }

    private class FakeNotificationService : INotificationService
    {
        public List<NotificationPayload> Notifications { get; } = new();
        public event Func<NotificationPayload, Task>? NotificationReceived;

        public Task ShowNotificationAsync(NotificationPayload payload, CancellationToken cancellationToken = default)
        {
            Notifications.Add(payload);
            NotificationReceived?.Invoke(payload);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<NotificationPayload>> GetHistoryAsync(int limit = 50, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NotificationPayload>>(Notifications.AsReadOnly());

        public Task ClearHistoryAsync(CancellationToken cancellationToken = default)
        {
            Notifications.Clear();
            return Task.CompletedTask;
        }
    }

    private class FakeAgentProvider : IAgentProvider
    {
        public string ProviderId => "fake";
        public string ProviderName => "Fake Agent";
        public ProviderConfig Config { get; set; } = new() { ProviderId = "fake", ModelName = "fake-model" };
        public Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(ConnectionTestResult.Success(10, "fake-model"));
        public Task<ChatCompletionResponse> GenerateResponseAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatCompletionResponse("fake response", "fake-model"));
        public async IAsyncEnumerable<string> StreamResponseAsync(ChatCompletionRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return "fake response";
            await Task.CompletedTask;
        }
    }

    private class FakeWebSearchService : IWebSearchService
    {
        public Task<WebSearchResponse> SearchAsync(string query, int maxResults = 5, CancellationToken cancellationToken = default)
        {
            var results = new List<WebSearchResultItem>
            {
                new("Niki AI Docs", "Comprehensive documentation for Niki AI widgets.", "https://nikiai.local/docs")
            };
            return Task.FromResult(new WebSearchResponse(query, results.AsReadOnly(), 1));
        }
    }

    #endregion

    [Fact]
    public async Task Widget1_ClockWidget_TogglesAndFormats()
    {
        var clock = new ClockWidget();
        Assert.Equal("clock", clock.Id);
        Assert.Equal(WidgetCategory.System, clock.Category);
        Assert.Equal(WidgetPresentationState.Active, clock.PresentationState);
        Assert.False(string.IsNullOrWhiteSpace(clock.PrimaryDisplayValue));

        // Toggle 12h/24h format
        await clock.ExecuteActionAsync();
        Assert.True(clock.Use24HourFormat);
        Assert.False(string.IsNullOrWhiteSpace(clock.PrimaryDisplayValue));
    }

    [Fact]
    public async Task Widget2_CalendarWidget_ValidEmptyAndActiveStates()
    {
        var repo = new FakeScheduledItemRepository();
        var calendar = new CalendarWidget(repo);

        await calendar.RefreshAsync();
        Assert.Equal(WidgetPresentationState.Empty, calendar.PresentationState);
        Assert.Equal("No upcoming events", calendar.PrimaryDisplayValue);

        // Add upcoming event
        repo.Items.Add(new ScheduledItem("item1", "Sprint Review", DateTimeOffset.Now.AddHours(2), itemType: ScheduledItemType.Reminder));
        await calendar.RefreshAsync();

        Assert.Equal(WidgetPresentationState.Active, calendar.PresentationState);
        Assert.Contains("Sprint Review", calendar.PrimaryDisplayValue);
    }

    [Fact]
    public async Task Widget3_WeatherWidget_ValidUnavailableAndActiveStates()
    {
        var offlineProvider = new LocalWeatherProvider();
        var weather = new WeatherWidget(offlineProvider);

        await weather.RefreshAsync();
        Assert.Equal(WidgetPresentationState.Unavailable, weather.PresentationState);
        Assert.Equal("Weather unavailable", weather.PrimaryDisplayValue);
    }

    [Fact]
    public async Task Widget4_QuickNoteWidget_ValidEmptyAndActiveStates()
    {
        var note = new QuickNoteWidget();
        Assert.Equal(WidgetPresentationState.Empty, note.PresentationState);
        Assert.Equal("Note empty", note.PrimaryDisplayValue);

        note.NoteContent = "Buy groceries and review Phase 11";
        Assert.Equal(WidgetPresentationState.Active, note.PresentationState);
        Assert.Contains("Buy groceries", note.PrimaryDisplayValue);

        // Clear action
        await note.ExecuteActionAsync();
        Assert.Equal(WidgetPresentationState.Empty, note.PresentationState);
        Assert.Equal("Note empty", note.PrimaryDisplayValue);
    }

    [Fact]
    public async Task Widget5_TasksWidget_ValidEmptyAndActiveStates()
    {
        var repo = new FakeTaskRepository();
        var tasks = new TasksWidget(repo);

        await tasks.RefreshAsync();
        Assert.Equal(WidgetPresentationState.Empty, tasks.PresentationState);
        Assert.Equal("No active tasks", tasks.PrimaryDisplayValue);

        var task = new AgentTask { Id = "t1", Title = "Build Widget Shell", Status = AgentTaskStatus.Running };
        repo.Tasks.Add(task);

        await tasks.RefreshAsync();
        Assert.Equal(WidgetPresentationState.Active, tasks.PresentationState);
        Assert.Contains("Build Widget Shell", tasks.PrimaryDisplayValue);
    }

    [Fact]
    public async Task Widget6_RemindersWidget_ValidEmptyAndActiveStates()
    {
        var repo = new FakeScheduledItemRepository();
        var reminders = new RemindersWidget(repo);

        await reminders.RefreshAsync();
        Assert.Equal(WidgetPresentationState.Empty, reminders.PresentationState);
        Assert.Equal("No reminders", reminders.PrimaryDisplayValue);

        repo.Items.Add(new ScheduledItem("r1", "Drink Water", DateTimeOffset.Now.AddMinutes(15), itemType: ScheduledItemType.Reminder));
        await reminders.RefreshAsync();

        Assert.Equal(WidgetPresentationState.Active, reminders.PresentationState);
        Assert.Contains("Drink Water", reminders.PrimaryDisplayValue);
    }

    [Fact]
    public async Task Widget7_SystemMonitorWidget_ValidMetrics()
    {
        var monitor = new SystemMonitorWidget();
        await monitor.RefreshAsync();

        Assert.Equal(WidgetPresentationState.Active, monitor.PresentationState);
        Assert.Contains("CPU:", monitor.PrimaryDisplayValue);
        Assert.Contains("RAM:", monitor.SecondaryDisplayValue);
    }

    [Fact]
    public async Task Widget8_MusicControlWidget_ValidIdleStateAndToggle()
    {
        var music = new MusicControlWidget();
        Assert.Equal(WidgetPresentationState.Empty, music.PresentationState);
        Assert.Equal("No media active", music.PrimaryDisplayValue);

        // Toggle action switches state
        await music.ExecuteActionAsync();
        Assert.Equal("Playing", music.PrimaryDisplayValue);
        Assert.Equal(WidgetPresentationState.Active, music.PresentationState);
    }

    [Fact]
    public async Task Widget9_ClipboardWidget_ValidEmptyAndActiveStates()
    {
        var clipService = new FakeClipboardService();
        var clipboard = new ClipboardWidget(clipService);

        await clipboard.RefreshAsync();
        Assert.Equal(WidgetPresentationState.Empty, clipboard.PresentationState);
        Assert.Equal("Clipboard empty", clipboard.PrimaryDisplayValue);

        clipService.Content = "Secure API Token Snippet";
        await clipboard.RefreshAsync();

        Assert.Equal(WidgetPresentationState.Active, clipboard.PresentationState);
        Assert.Contains("Secure API Token", clipboard.PrimaryDisplayValue);

        // Clear action
        await clipboard.ExecuteActionAsync();
        Assert.Equal(WidgetPresentationState.Empty, clipboard.PresentationState);
        Assert.Equal("Clipboard empty", clipboard.PrimaryDisplayValue);
    }

    [Fact]
    public async Task Widget10_FocusTimerWidget_CountsDownAndNotifies()
    {
        var notifService = new FakeNotificationService();
        var timer = new FocusTimerWidget(notifService);

        Assert.Equal(WidgetPresentationState.Empty, timer.PresentationState);
        Assert.Equal("25:00", timer.PrimaryDisplayValue);

        // Start timer
        await timer.ExecuteActionAsync();
        Assert.True(timer.IsTimerRunning);
        Assert.Equal(WidgetPresentationState.Active, timer.PresentationState);

        // Simulate 1 tick
        await timer.RefreshAsync();
        Assert.Equal("24:59", timer.PrimaryDisplayValue);

        // Reset
        timer.Reset();
        Assert.False(timer.IsTimerRunning);
        Assert.Equal("25:00", timer.PrimaryDisplayValue);
    }

    [Fact]
    public async Task Widget11_AiTaskProgressWidget_ValidIdleAndActiveStates()
    {
        var provider = new FakeAgentProvider();
        var taskRepo = new FakeTaskRepository();
        var aiWidget = new AiTaskProgressWidget(provider, taskRepo);

        await aiWidget.RefreshAsync();
        Assert.Equal(WidgetPresentationState.Empty, aiWidget.PresentationState);
        Assert.Equal("Agent Idle", aiWidget.PrimaryDisplayValue);

        taskRepo.Tasks.Add(new AgentTask { Id = "t2", Title = "Analyzing Codebase", Status = AgentTaskStatus.Running });
        await aiWidget.RefreshAsync();

        Assert.Equal(WidgetPresentationState.Active, aiWidget.PresentationState);
        Assert.Contains("Analyzing Codebase", aiWidget.PrimaryDisplayValue);
    }

    [Fact]
    public async Task Widget12_WebResultsWidget_ValidEmptyAndSearchStates()
    {
        var searchService = new FakeWebSearchService();
        var webWidget = new WebResultsWidget(searchService);

        await webWidget.RefreshAsync();
        Assert.Equal(WidgetPresentationState.Empty, webWidget.PresentationState);
        Assert.Equal("No recent searches", webWidget.PrimaryDisplayValue);

        webWidget.SetLastQuery("Niki AI Widgets");
        await webWidget.RefreshAsync();

        Assert.Equal(WidgetPresentationState.Active, webWidget.PresentationState);
        Assert.Equal("Niki AI Docs", webWidget.PrimaryDisplayValue);
    }
}
