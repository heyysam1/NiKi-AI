using NikiAI.Core.Security;
using NikiAI.Core.Tools;
using NikiAI.Core.Workflows;
using NikiAI.Security;
using NikiAI.Tools;
using Xunit;

namespace NikiAI.Workflows.Tests;

public class NoToolRetryTests
{
    private readonly InMemoryWorkflowRepository _workflowRepo = new();
    private readonly ToolRegistry _toolRegistry = new();
    private readonly IPermissionEngine _permEngine;
    private readonly ToolExecutor _toolExecutor;
    private readonly InMemoryTimelineRepository _timelineRepo = new();

    public NoToolRetryTests()
    {
        _permEngine = new PermissionEngine();
        _toolExecutor = new ToolExecutor(_toolRegistry, _permEngine, new FakeToolAuditLogger());
    }

    [Fact]
    public async Task FailedToolAction_IsNeverAutomaticallyRetried()
    {
        var failingTool = new MockTool("tool_flaky", "Flaky Tool")
        {
            ExecutionHandler = call => ToolResult.Failure(call.CallId, "tool_flaky", "Transient network timeout", TimeSpan.FromMilliseconds(5))
        };
        _toolRegistry.RegisterTool(failingTool);

        var wf = new WorkflowDefinition(
            Id: "wf-no-retry-test",
            Name: "No Retry Enforcement Test",
            Description: "Verifies failed tool is executed exactly once without retry",
            Category: "Test",
            Trigger: TriggerType.Manual,
            TriggerConfigJson: null,
            Inputs: new List<string>(),
            Actions: new List<WorkflowActionDefinition>
            {
                new("step-flaky", "Flaky Step", ActionType.ToolCall, "tool_flaky", "{}", false, null, 5)
            },
            TimeoutSeconds: 30,
            CompletionBehavior: CompletionBehavior.Silent,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow
        );
        await _workflowRepo.CreateWorkflowAsync(wf);

        var engine = new WorkflowEngine(
            _workflowRepo, _toolRegistry, _toolExecutor, _permEngine,
            _timelineRepo, null, null, null);

        var result = await engine.ExecuteWorkflowAsync(wf.Id);

        // Verification: Status must be Failed, and failingTool MUST have been called exactly once!
        Assert.Equal(WorkflowExecutionStatus.Failed, result.Status);
        Assert.Equal(1, failingTool.ExecutionCount);
    }

    [Fact]
    public void WorkflowDefinition_HasNoRetryPolicyProperties()
    {
        // Reflection check ensuring no RetryPolicy exists on WorkflowDefinition or WorkflowActionDefinition in Phase 13
        var wfProperties = typeof(WorkflowDefinition).GetProperties().Select(p => p.Name).ToList();
        var actProperties = typeof(WorkflowActionDefinition).GetProperties().Select(p => p.Name).ToList();

        Assert.DoesNotContain("RetryPolicy", wfProperties);
        Assert.DoesNotContain("MaxRetries", wfProperties);
        Assert.DoesNotContain("RetryCount", wfProperties);

        Assert.DoesNotContain("RetryPolicy", actProperties);
        Assert.DoesNotContain("IsIdempotent", actProperties);
        Assert.DoesNotContain("MaxRetries", actProperties);
    }
}
