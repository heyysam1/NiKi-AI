using NikiAI.Core.Notifications;
using NikiAI.Core.Security;
using NikiAI.Core.Tools;
using NikiAI.Core.Workflows;
using NikiAI.Core.Memory;

namespace NikiAI.Workflows.Tests;

public class InMemoryWorkflowRepository : IWorkflowRepository
{
    private readonly Dictionary<string, WorkflowDefinition> _workflows = new();
    private readonly List<WorkflowRunRecord> _runs = new();

    public Task<WorkflowDefinition> CreateWorkflowAsync(WorkflowDefinition workflow, CancellationToken cancellationToken = default)
    {
        _workflows[workflow.Id] = workflow;
        return Task.FromResult(workflow);
    }

    public Task<WorkflowDefinition?> GetWorkflowByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        _workflows.TryGetValue(id, out var wf);
        return Task.FromResult(wf);
    }

    public Task<IReadOnlyList<WorkflowDefinition>> GetAllWorkflowsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<WorkflowDefinition>>(_workflows.Values.ToList());
    }

    public Task UpdateWorkflowAsync(WorkflowDefinition workflow, CancellationToken cancellationToken = default)
    {
        _workflows[workflow.Id] = workflow;
        return Task.CompletedTask;
    }

    public Task DeleteWorkflowAsync(string id, CancellationToken cancellationToken = default)
    {
        _workflows.Remove(id);
        return Task.CompletedTask;
    }

    public Task RecordRunAsync(WorkflowRunRecord runRecord, CancellationToken cancellationToken = default)
    {
        var existing = _runs.FindIndex(r => r.RunId == runRecord.RunId);
        if (existing >= 0)
        {
            _runs[existing] = runRecord;
        }
        else
        {
            _runs.Add(runRecord);
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<WorkflowRunRecord>> GetRunHistoryAsync(string workflowId, int limit = 50, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<WorkflowRunRecord>>(
            _runs.Where(r => r.WorkflowId == workflowId).OrderByDescending(r => r.StartedAt).Take(limit).ToList()
        );
    }

    public Task<WorkflowRunRecord?> GetRunByIdAsync(string runId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_runs.FirstOrDefault(r => r.RunId == runId));
    }
}

public class MockTool : ITool
{
    public string Id { get; }
    public string Name { get; }
    public string Description => "Mock Tool for Testing";
    public ToolRiskLevel RiskLevel { get; set; } = ToolRiskLevel.LowRiskReversible;
    public TimeSpan DefaultTimeout => TimeSpan.FromSeconds(5);
    public string InputSchemaJson => "{\"type\":\"object\"}";

    public int ExecutionCount { get; private set; }
    public Func<ToolCall, ToolResult>? ExecutionHandler { get; set; }

    public MockTool(string id, string name = "Mock Tool")
    {
        Id = id;
        Name = name;
    }

    public Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken = default)
    {
        ExecutionCount++;
        if (ExecutionHandler != null)
        {
            return Task.FromResult(ExecutionHandler(call));
        }
        return Task.FromResult(ToolResult.Success(call.CallId, Id, "{\"success\":true}", TimeSpan.FromMilliseconds(10)));
    }
}

public class InMemoryTimelineRepository : ITimelineRepository
{
    private readonly List<TimelineEvent> _events = new();

    public Task LogEventAsync(TimelineEvent timelineEvent, CancellationToken cancellationToken = default)
    {
        _events.Add(timelineEvent);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<TimelineEvent>> GetRecentEventsAsync(int limit = 50, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<TimelineEvent>>(_events.OrderByDescending(e => e.Timestamp).Take(limit).ToList());
    }

    public Task<IReadOnlyList<TimelineEvent>> GetEventsByRelatedIdAsync(string relatedId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<TimelineEvent>>(_events.Where(e => e.RelatedId == relatedId).ToList());
    }

    public Task<int> ClearAllEventsAsync(CancellationToken cancellationToken = default)
    {
        var count = _events.Count;
        _events.Clear();
        return Task.FromResult(count);
    }
}

public class MockNotificationService : INotificationService
{
    public List<NotificationPayload> ShownNotifications { get; } = new();
    public event Func<NotificationPayload, Task>? NotificationReceived;

    public Task ShowNotificationAsync(NotificationPayload payload, CancellationToken cancellationToken = default)
    {
        ShownNotifications.Add(payload);
        NotificationReceived?.Invoke(payload);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<NotificationPayload>> GetHistoryAsync(int limit = 50, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<NotificationPayload>>(ShownNotifications.Take(limit).ToList());
    }

    public Task ClearHistoryAsync(CancellationToken cancellationToken = default)
    {
        ShownNotifications.Clear();
        return Task.CompletedTask;
    }
}

public class FakeToolAuditLogger : IToolAuditLogger
{
    private readonly List<ToolAuditRecord> _records = new();

    public Task LogExecutionAsync(ToolAuditRecord record, CancellationToken cancellationToken = default)
    {
        _records.Add(record);
        return Task.CompletedTask;
    }

    public IReadOnlyList<ToolAuditRecord> GetRecentRecords(int limit = 50) => _records.TakeLast(limit).ToList();
}
