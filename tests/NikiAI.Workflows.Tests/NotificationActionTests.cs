using NikiAI.Core.Lifecycle;
using NikiAI.Core.Notifications;
using NikiAI.Core.Security;
using NikiAI.Core.Tools;
using NikiAI.Core.Workflows;
using NikiAI.Security;
using NikiAI.Tools;
using Xunit;

namespace NikiAI.Workflows.Tests;

public class NotificationActionTests
{
    [Fact]
    public async Task NotificationAction_ReusesNotificationService_AndSeparatesPetSignal()
    {
        var workflowRepo = new InMemoryWorkflowRepository();
        var toolRegistry = new ToolRegistry();
        var permEngine = new PermissionEngine();
        var toolExecutor = new ToolExecutor(toolRegistry, permEngine, new FakeToolAuditLogger());
        var timelineRepo = new InMemoryTimelineRepository();
        var notifService = new MockNotificationService();
        var signalHub = new TaskLifecycleSignalHub();

        var capturedSignals = new List<TaskLifecycleSignal>();
        signalHub.SignalEmitted += capturedSignals.Add;

        var wf = new WorkflowDefinition(
            Id: "wf-notif-test",
            Name: "Notification Test Workflow",
            Description: "Tests notification execution",
            Category: "Test",
            Trigger: TriggerType.Manual,
            TriggerConfigJson: null,
            Inputs: new List<string>(),
            Actions: new List<WorkflowActionDefinition>
            {
                new("step-notif", "Notify Complete", ActionType.Notification, null, "{\"message\":\"All work completed successfully.\"}", false, null, 5)
            },
            TimeoutSeconds: 30,
            CompletionBehavior: CompletionBehavior.Notify,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow
        );
        await workflowRepo.CreateWorkflowAsync(wf);

        var engine = new WorkflowEngine(
            workflowRepo, toolRegistry, toolExecutor, permEngine,
            timelineRepo, notifService, null, signalHub);

        var result = await engine.ExecuteWorkflowAsync(wf.Id);

        Assert.Equal(WorkflowExecutionStatus.Completed, result.Status);

        // 1. Verify notification was shown through INotificationService
        Assert.Single(notifService.ShownNotifications);
        var notif = notifService.ShownNotifications[0];
        Assert.Equal("Notification Test Workflow", notif.Title);
        Assert.Equal("All work completed successfully.", notif.SummaryMessage);

        // 2. Verify WorkflowNotification signal was emitted to Pet
        var notifSignal = capturedSignals.FirstOrDefault(s => s.SignalType == TaskLifecycleSignalType.WorkflowNotification);
        Assert.NotNull(notifSignal);

        // 3. Verify signal contains ONLY SignalType, opaque Guid SourceId, Timestamp (no notification text)
        Assert.NotEqual(Guid.Empty, notifSignal.SourceId);
        Assert.True(notifSignal.Timestamp <= DateTimeOffset.UtcNow);
    }
}
