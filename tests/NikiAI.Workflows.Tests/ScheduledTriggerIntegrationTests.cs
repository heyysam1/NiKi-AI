using NikiAI.Core.Scheduler;
using NikiAI.Core.Security;
using NikiAI.Core.Tools;
using NikiAI.Core.Workflows;
using NikiAI.Security;
using NikiAI.Tools;
using Xunit;

namespace NikiAI.Workflows.Tests;

public class MockSchedulerService : ISchedulerService
{
    public event Func<ScheduledItem, Task>? ItemTriggered;

    public Task TriggerItemAsync(ScheduledItem item)
    {
        return ItemTriggered != null ? ItemTriggered(item) : Task.CompletedTask;
    }

    public Task ScheduleAsync(ScheduledItem item, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task CancelAsync(string id, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SnoozeAsync(string id, TimeSpan duration, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task DismissAsync(string id, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<ScheduledItem?> GetItemByIdAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult<ScheduledItem?>(null);
    public Task<IReadOnlyList<ScheduledItem>> GetPendingItemsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ScheduledItem>>(new List<ScheduledItem>());
    public Task<IReadOnlyList<ScheduledItem>> GetAllItemsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ScheduledItem>>(new List<ScheduledItem>());
    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public class ScheduledTriggerIntegrationTests
{
    [Fact]
    public async Task ScheduledItem_TriggersWorkflowExecutionSafely()
    {
        var workflowRepo = new InMemoryWorkflowRepository();
        var toolRegistry = new ToolRegistry();
        var permEngine = new PermissionEngine();
        var toolExecutor = new ToolExecutor(toolRegistry, permEngine, new FakeToolAuditLogger());
        var timelineRepo = new InMemoryTimelineRepository();

        var mockTool = new MockTool("tool_scheduled_test");
        toolRegistry.RegisterTool(mockTool);

        var wf = new WorkflowDefinition(
            Id: "wf-scheduled-run",
            Name: "Scheduled Run",
            Description: "Workflow triggered on schedule",
            Category: "Work",
            Trigger: TriggerType.Scheduled,
            TriggerConfigJson: null,
            Inputs: new List<string>(),
            Actions: new List<WorkflowActionDefinition>
            {
                new("step-sched", "Scheduled Action", ActionType.ToolCall, "tool_scheduled_test", "{}", false, null, 5)
            },
            TimeoutSeconds: 30,
            CompletionBehavior: CompletionBehavior.Silent,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow
        );
        await workflowRepo.CreateWorkflowAsync(wf);

        var engine = new WorkflowEngine(
            workflowRepo, toolRegistry, toolExecutor, permEngine,
            timelineRepo, null, null, null);

        var schedulerService = new MockSchedulerService();
        var listener = new ScheduledWorkflowTriggerListener(schedulerService, engine);

        // Simulate scheduler item trigger
        var item = new ScheduledItem(
            id: "item-sched-1",
            title: "Scheduled Run Item",
            scheduledTime: DateTimeOffset.UtcNow,
            isRecurring: false,
            payloadJson: "{\"workflow_id\":\"wf-scheduled-run\"}"
        );

        await schedulerService.TriggerItemAsync(item);

        // Verify the workflow executed and the tool ran
        Assert.Equal(1, mockTool.ExecutionCount);

        var history = await workflowRepo.GetRunHistoryAsync("wf-scheduled-run");
        Assert.Single(history);
        Assert.Equal(WorkflowExecutionStatus.Completed, history[0].Status);
    }
}
