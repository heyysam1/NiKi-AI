using NikiAI.Core.Lifecycle;
using NikiAI.Core.Tasks;
using Xunit;

namespace NikiAI.Workflows.Tests;

public class TaskLifecycleSignalPrivacyTests
{
    private class FakeTaskRepository : ITaskRepository
    {
        public event Action<string, AgentTaskStatus, AgentTaskStatus>? StatusChanged;

        public void TriggerStatusChanged(string taskId, AgentTaskStatus previous, AgentTaskStatus current)
        {
            StatusChanged?.Invoke(taskId, previous, current);
        }

        public Task<AgentTask> CreateAsync(AgentTask task, CancellationToken cancellationToken = default) => Task.FromResult(task);
        public Task<AgentTask?> GetByIdAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult<AgentTask?>(null);
        public Task<IReadOnlyList<AgentTask>> GetAllAsync(bool includeArchived = false, int limit = 100, int offset = 0, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AgentTask>>(new List<AgentTask>());
        public Task<IReadOnlyList<AgentTask>> GetByStatusAsync(AgentTaskStatus status, bool includeArchived = false, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AgentTask>>(new List<AgentTask>());
        public Task UpdateAsync(AgentTask task, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task TransitionStatusAsync(string taskId, AgentTaskStatus newStatus, string? message = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ArchiveAsync(string taskId, string? reason = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task PurgePermanentlyAsync(string taskId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task AddEventAsync(TaskEvent taskEvent, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<TaskEvent>> GetEventsAsync(string taskId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<TaskEvent>>(new List<TaskEvent>());
    }

    [Fact]
    public void TaskLifecycleSignal_ContainsOnlySignalTypeSourceIdTimestamp_ZeroTitlesOrPayloads()
    {
        var taskRepo = new FakeTaskRepository();
        var hub = new TaskLifecycleSignalHub();
        hub.AttachTaskRepository(taskRepo);

        var capturedSignals = new List<TaskLifecycleSignal>();
        hub.SignalEmitted += capturedSignals.Add;

        var taskGuid = Guid.NewGuid();
        taskRepo.TriggerStatusChanged(taskGuid.ToString(), AgentTaskStatus.Pending, AgentTaskStatus.Running);

        Assert.Single(capturedSignals);
        var signal = capturedSignals[0];

        // 1. Verify exact fields
        Assert.Equal(TaskLifecycleSignalType.TaskStarted, signal.SignalType);
        Assert.Equal(taskGuid, signal.SourceId);
        Assert.True(signal.Timestamp <= DateTimeOffset.UtcNow);

        // 2. Reflection check verifying NO text, title, or payload properties exist on TaskLifecycleSignal
        var properties = typeof(TaskLifecycleSignal).GetProperties();
        var propNames = properties.Select(p => p.Name).ToList();

        Assert.Equal(3, propNames.Count);
        Assert.Contains("SignalType", propNames);
        Assert.Contains("SourceId", propNames);
        Assert.Contains("Timestamp", propNames);

        Assert.DoesNotContain("Title", propNames);
        Assert.DoesNotContain("Prompt", propNames);
        Assert.DoesNotContain("Payload", propNames);
        Assert.DoesNotContain("Message", propNames);
        Assert.DoesNotContain("Content", propNames);
    }
}
