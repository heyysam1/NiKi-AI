using NikiAI.Core.Lifecycle;
using NikiAI.Core.Security;
using NikiAI.Core.Tools;
using NikiAI.Core.Workflows;
using NikiAI.Security;
using NikiAI.Tools;
using Xunit;

namespace NikiAI.Workflows.Tests;

public class TriggerSecurityTests
{
    private readonly InMemoryWorkflowRepository _workflowRepo = new();
    private readonly ToolRegistry _toolRegistry = new();
    private readonly IPermissionEngine _permEngine;
    private readonly ToolExecutor _toolExecutor;
    private readonly InMemoryTimelineRepository _timelineRepo = new();

    public TriggerSecurityTests()
    {
        _permEngine = new PermissionEngine();
        _toolExecutor = new ToolExecutor(_toolRegistry, _permEngine, new FakeToolAuditLogger());
    }

    private class DenyingPromptHandler : IApprovalPromptHandler
    {
        public Task<ApprovalDecisionResult> RequestApprovalAsync(ApprovalRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ApprovalDecisionResult.Denied("Denied by test"));
        }
    }

    [Fact]
    public async Task ScheduledTrigger_WhenApprovalRequired_CannotBypassPermissionEngine()
    {
        var sensitiveTool = new MockTool("tool_restricted", "Restricted Tool") { RiskLevel = ToolRiskLevel.HighRisk };
        _toolRegistry.RegisterTool(sensitiveTool);

        var wf = new WorkflowDefinition(
            Id: "wf-restricted-scheduled",
            Name: "Restricted Scheduled Workflow",
            Description: "Requires approval",
            Category: "Test",
            Trigger: TriggerType.Scheduled,
            TriggerConfigJson: null,
            Inputs: new List<string>(),
            Actions: new List<WorkflowActionDefinition>
            {
                new("step-high", "Restricted Action", ActionType.ToolCall, "tool_restricted", "{}", true, null, 5)
            },
            TimeoutSeconds: 30,
            CompletionBehavior: CompletionBehavior.Silent,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow
        );
        await _workflowRepo.CreateWorkflowAsync(wf);

        var denyingPromptHandler = new DenyingPromptHandler();
        var engine = new WorkflowEngine(
            _workflowRepo, _toolRegistry, _toolExecutor, _permEngine,
            _timelineRepo, null, denyingPromptHandler, null);

        // Execute scheduled trigger
        var result = await engine.ExecuteWorkflowAsync(wf.Id);

        // Verification: The execution MUST fail because approval was denied
        Assert.Equal(WorkflowExecutionStatus.Failed, result.Status);
        // The underlying tool MUST NOT have run
        Assert.Equal(0, sensitiveTool.ExecutionCount);
    }

    [Fact]
    public async Task EventTrigger_WhenApprovalRequired_CannotBypassPermissionEngine()
    {
        var sensitiveTool = new MockTool("tool_event_restricted", "Event Tool") { RiskLevel = ToolRiskLevel.HighRisk };
        _toolRegistry.RegisterTool(sensitiveTool);

        var wf = new WorkflowDefinition(
            Id: "wf-restricted-event",
            Name: "Restricted Event Workflow",
            Description: "Requires approval on event",
            Category: "Test",
            Trigger: TriggerType.Event,
            TriggerConfigJson: null,
            Inputs: new List<string>(),
            Actions: new List<WorkflowActionDefinition>
            {
                new("step-evt", "Event Action", ActionType.ToolCall, "tool_event_restricted", "{}", true, null, 5)
            },
            TimeoutSeconds: 30,
            CompletionBehavior: CompletionBehavior.Silent,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow
        );
        await _workflowRepo.CreateWorkflowAsync(wf);

        var denyingPromptHandler = new DenyingPromptHandler();
        var engine = new WorkflowEngine(
            _workflowRepo, _toolRegistry, _toolExecutor, _permEngine,
            _timelineRepo, null, denyingPromptHandler, null);

        var result = await engine.ExecuteWorkflowAsync(wf.Id);

        Assert.Equal(WorkflowExecutionStatus.Failed, result.Status);
        Assert.Equal(0, sensitiveTool.ExecutionCount);
    }
}
