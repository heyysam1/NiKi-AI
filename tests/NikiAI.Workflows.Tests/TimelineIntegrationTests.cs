using NikiAI.Core.Security;
using NikiAI.Core.Tools;
using NikiAI.Core.Workflows;
using NikiAI.Security;
using NikiAI.Tools;
using Xunit;

namespace NikiAI.Workflows.Tests;

public class TimelineIntegrationTests
{
    [Fact]
    public async Task WorkflowMilestones_LogOperationalMetadataOnly_ExcludesSensitiveData()
    {
        var workflowRepo = new InMemoryWorkflowRepository();
        var toolRegistry = new ToolRegistry();
        var permEngine = new PermissionEngine();
        var toolExecutor = new ToolExecutor(toolRegistry, permEngine, new FakeToolAuditLogger());
        var timelineRepo = new InMemoryTimelineRepository();

        const string sensitiveInput = "SensitiveResearchTopicData";
        const string secretText = "SecretClipboardContent123";

        var tool = new MockTool("tool_research")
        {
            ExecutionHandler = call => ToolResult.Success(call.CallId, "tool_research", $"{{\"secret\":\"{secretText}\"}}", TimeSpan.FromMilliseconds(5))
        };
        toolRegistry.RegisterTool(tool);

        var wf = new WorkflowDefinition(
            Id: "wf-timeline-test",
            Name: "Timeline Verification",
            Description: "Tests operational timeline logging",
            Category: "Test",
            Trigger: TriggerType.Manual,
            TriggerConfigJson: null,
            Inputs: new List<string> { "input" },
            Actions: new List<WorkflowActionDefinition>
            {
                new("step-1", "Research Action", ActionType.ToolCall, "tool_research", $"{{\"query\":\"{sensitiveInput}\"}}", false, null, 5)
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

        var inputs = new Dictionary<string, string> { ["input"] = sensitiveInput };
        var runResult = await engine.ExecuteWorkflowAsync(wf.Id, inputs);

        Assert.Equal(WorkflowExecutionStatus.Completed, runResult.Status);

        var events = await timelineRepo.GetRecentEventsAsync(10);
        Assert.NotEmpty(events);

        foreach (var evt in events)
        {
            Assert.Contains(evt.EventType, new[] { "WorkflowStarted", "WorkflowCompleted", "WorkflowFailed" });
            Assert.Equal("WorkflowEngine", evt.Source);
            Assert.Equal(runResult.RunId, evt.RelatedId);

            // Verify DetailsJson contains operational metadata only
            Assert.NotNull(evt.DetailsJson);
            Assert.Contains("workflow_id", evt.DetailsJson);
            Assert.Contains("run_id", evt.DetailsJson);
            Assert.Contains("status", evt.DetailsJson);

            // Assert strictly NO sensitive input, secret or output in timeline
            Assert.DoesNotContain(sensitiveInput, evt.DetailsJson);
            Assert.DoesNotContain(secretText, evt.DetailsJson);
            Assert.DoesNotContain(sensitiveInput, evt.Summary);
            Assert.DoesNotContain(secretText, evt.Summary);
        }
    }
}
