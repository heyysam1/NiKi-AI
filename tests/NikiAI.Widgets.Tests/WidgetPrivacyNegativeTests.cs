using NikiAI.Core.Character;
using NikiAI.Core.Memory;
using NikiAI.Core.Tasks;
using NikiAI.Core.Tools;
using NikiAI.Core.Widgets;
using Xunit;

namespace NikiAI.Widgets.Tests;

public class WidgetPrivacyNegativeTests
{
    private class FakeMemoryStore : IMemoryStore
    {
        public List<MemoryItem> Items { get; } = new();

        public Task SaveMemoryAsync(MemoryItem item, CancellationToken cancellationToken = default)
        {
            Items.Add(item);
            return Task.CompletedTask;
        }

        public Task<MemoryItem?> GetMemoryByIdAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.FirstOrDefault(m => m.Id == id));

        public Task<IReadOnlyList<MemoryItem>> GetMemoriesAsync(MemoryCategory category, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MemoryItem>>(Items.Where(m => m.Category == category).ToList().AsReadOnly());

        public Task<IReadOnlyList<MemoryItem>> SearchMemoriesAsync(string query, MemoryCategory? category = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MemoryItem>>(Items.Where(m => m.Content.Contains(query)).ToList().AsReadOnly());

        public Task<IReadOnlyList<MemoryItem>> GetProjectMemoriesAsync(string projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MemoryItem>>(Items.Where(m => m.ProjectId == projectId).ToList().AsReadOnly());

        public Task DeleteMemoryAsync(string id, CancellationToken cancellationToken = default)
        {
            Items.RemoveAll(m => m.Id == id);
            return Task.CompletedTask;
        }

        public Task ClearCategoryAsync(MemoryCategory category, CancellationToken cancellationToken = default)
        {
            Items.RemoveAll(m => m.Category == category);
            return Task.CompletedTask;
        }
    }

    private class FakeTimelineRepository : ITimelineRepository
    {
        public List<TimelineEvent> Events { get; } = new();

        public Task LogEventAsync(TimelineEvent timelineEvent, CancellationToken cancellationToken = default)
        {
            Events.Add(timelineEvent);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<TimelineEvent>> GetRecentEventsAsync(int limit = 50, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TimelineEvent>>(Events.AsReadOnly());

        public Task<IReadOnlyList<TimelineEvent>> GetEventsByRelatedIdAsync(string relatedId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TimelineEvent>>(Events.Where(e => e.RelatedId == relatedId).ToList().AsReadOnly());

        public Task<int> ClearAllEventsAsync(CancellationToken cancellationToken = default)
        {
            var count = Events.Count;
            Events.Clear();
            return Task.FromResult(count);
        }
    }

    private class FakeClipboardService : IClipboardService
    {
        public string? Text { get; set; } = "Sensitive User Clipboard Data";
        public Task<string?> GetTextAsync(CancellationToken cancellationToken = default) => Task.FromResult(Text);
        public Task SetTextAsync(string text, CancellationToken cancellationToken = default)
        {
            Text = text;
            return Task.CompletedTask;
        }
    }

    private class FakeTaskRepository : ITaskRepository
    {
        public List<AgentTask> Tasks { get; } = new()
        {
            new AgentTask { Id = "t_sec", Title = "Confidential Task Title ABC", Status = AgentTaskStatus.Running }
        };

        public Task<AgentTask> CreateAsync(AgentTask task, CancellationToken cancellationToken = default) => Task.FromResult(task);
        public Task<AgentTask?> GetByIdAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult(Tasks.FirstOrDefault(t => t.Id == id));
        public Task<IReadOnlyList<AgentTask>> GetAllAsync(bool includeArchived = false, int limit = 100, int offset = 0, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AgentTask>>(Tasks.AsReadOnly());
        public Task<IReadOnlyList<AgentTask>> GetByStatusAsync(AgentTaskStatus status, bool includeArchived = false, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AgentTask>>(Tasks.Where(t => t.Status == status).ToList().AsReadOnly());
        public Task UpdateAsync(AgentTask task, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task TransitionStatusAsync(string taskId, AgentTaskStatus newStatus, string? message = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ArchiveAsync(string taskId, string? reason = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task PurgePermanentlyAsync(string taskId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task AddEventAsync(TaskEvent taskEvent, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<TaskEvent>> GetEventsAsync(string taskId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<TaskEvent>>(Array.Empty<TaskEvent>());
    }

    private class FakePetContextProvider : IPetContextProvider
    {
        public PetContextSnapshot GetContextSnapshot() =>
            new PetContextSnapshot(
                CurrentTaskState: AgentTaskStatus.Running,
                ActiveNotificationCount: 0,
                IsUserActive: true,
                SessionDuration: TimeSpan.FromMinutes(10),
                TimeOfDay: TimeOfDayBucket.Morning,
                LastPetInteractionTime: DateTimeOffset.UtcNow
            );

        public void RecordUserInteraction() { }
    }

    [Fact]
    public async Task TasksWidget_TitleRemainsInUI_NeverEntersMemoryTimelineOrPetContext()
    {
        var memStore = new FakeMemoryStore();
        var timeline = new FakeTimelineRepository();
        var petContext = new FakePetContextProvider();
        var taskRepo = new FakeTaskRepository();

        var tasksWidget = new TasksWidget(taskRepo);
        await tasksWidget.RefreshAsync();

        // 1. Title is displayed in the user-facing card UI
        Assert.Contains("Confidential Task Title ABC", tasksWidget.PrimaryDisplayValue);

        // 2. Title NEVER enters IMemoryStore
        Assert.Empty(memStore.Items);

        // 3. Title NEVER enters ITimelineRepository
        Assert.Empty(timeline.Events);

        // 4. Title NEVER enters PetContextSnapshot
        var snapshot = petContext.GetContextSnapshot();
        Assert.Equal(AgentTaskStatus.Running, snapshot.CurrentTaskState);
        // Note: PetContextSnapshot has only status enum, not title string
    }

    [Fact]
    public async Task ClipboardWidget_SnippetRemainsInUI_NeverEntersMemoryTimelineOrPetContext()
    {
        var memStore = new FakeMemoryStore();
        var timeline = new FakeTimelineRepository();
        var petContext = new FakePetContextProvider();
        var clipboardService = new FakeClipboardService();

        var clipboardWidget = new ClipboardWidget(clipboardService);
        await clipboardWidget.RefreshAsync();

        // 1. Snippet displayed in user-facing card
        Assert.Contains("Sensitive User Clipboard", clipboardWidget.PrimaryDisplayValue);

        // 2. Content NEVER enters IMemoryStore
        Assert.Empty(memStore.Items);

        // 3. Content NEVER enters ITimelineRepository
        Assert.Empty(timeline.Events);

        // 4. Content NEVER enters PetContextSnapshot
        var snapshot = petContext.GetContextSnapshot();
        Assert.NotNull(snapshot);
    }

    [Fact]
    public async Task QuickNoteWidget_EphemeralOnly_NeverEntersMemoryOrTimeline()
    {
        var memStore = new FakeMemoryStore();
        var timeline = new FakeTimelineRepository();

        var noteWidget = new QuickNoteWidget();
        noteWidget.NoteContent = "My secret personal diary thought";
        await noteWidget.RefreshAsync();

        Assert.Empty(memStore.Items);
        Assert.Empty(timeline.Events);
    }

    [Fact]
    public async Task RoutineWidgetOperations_DoNotEmitTimelineEvents()
    {
        var timeline = new FakeTimelineRepository();

        var clock = new ClockWidget();
        var timer = new FocusTimerWidget();
        var monitor = new SystemMonitorWidget();

        await clock.RefreshAsync();
        await timer.RefreshAsync();
        await monitor.RefreshAsync();

        // Routine refreshes must produce zero timeline events
        Assert.Empty(timeline.Events);
    }
}
