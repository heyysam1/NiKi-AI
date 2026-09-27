using NikiAI.App.Services;
using NikiAI.Core.Security;
using NikiAI.Core.Tools;
using NikiAI.Core.Workflows;
using NikiAI.Security;
using NikiAI.Tools;
using Xunit;

namespace NikiAI.Workflows.Tests;

public class HotkeyTriggerIntegrationTests
{
    private class DenyingPromptHandler : IApprovalPromptHandler
    {
        public Task<ApprovalDecisionResult> RequestApprovalAsync(ApprovalRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ApprovalDecisionResult.Denied("Denied by test"));
        }
    }

    [Fact]
    public async Task HotkeyTrigger_InvokesWorkflowEngine_Successfully()
    {
        var workflowRepo = new InMemoryWorkflowRepository();
        var toolRegistry = new ToolRegistry();
        var permEngine = new PermissionEngine();
        var toolExecutor = new ToolExecutor(toolRegistry, permEngine, new FakeToolAuditLogger());
        var timelineRepo = new InMemoryTimelineRepository();

        var mockTool = new MockTool("tool_hotkey_test");
        toolRegistry.RegisterTool(mockTool);

        var wf = new WorkflowDefinition(
            Id: "wf-hotkey-run",
            Name: "Hotkey Run Workflow",
            Description: "Workflow triggered by global hotkey",
            Category: "Work",
            Trigger: TriggerType.Hotkey,
            TriggerConfigJson: "{\"key\":\"N\",\"modifiers\":[\"Win\",\"Alt\"]}",
            Inputs: new List<string>(),
            Actions: new List<WorkflowActionDefinition>
            {
                new("step-hotkey", "Hotkey Action", ActionType.ToolCall, "tool_hotkey_test", "{}", false, null, 5)
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

        var hotkeyManager = new GlobalHotkeyManager();
        var executionTcs = new TaskCompletionSource<WorkflowRunRecord>();

        hotkeyManager.WorkflowHotkeyPressed += async (wfId) =>
        {
            var run = await engine.ExecuteWorkflowAsync(wfId);
            executionTcs.TrySetResult(run);
        };

        // Simulate global hotkey invocation
        hotkeyManager.TriggerWorkflowHotkey(wf.Id);

        var completedRun = await Task.WhenAny(executionTcs.Task, Task.Delay(3000));
        Assert.Same(executionTcs.Task, completedRun);

        var runResult = await executionTcs.Task;
        Assert.Equal(WorkflowExecutionStatus.Completed, runResult.Status);
        Assert.Equal(1, mockTool.ExecutionCount);

        var history = await workflowRepo.GetRunHistoryAsync(wf.Id);
        Assert.Single(history);
        Assert.Equal(WorkflowExecutionStatus.Completed, history[0].Status);
    }

    [Fact]
    public async Task HotkeyTrigger_WhenActionRequiresApproval_CannotBypassPermissionEngine()
    {
        var workflowRepo = new InMemoryWorkflowRepository();
        var toolRegistry = new ToolRegistry();
        var permEngine = new PermissionEngine();
        var toolExecutor = new ToolExecutor(toolRegistry, permEngine, new FakeToolAuditLogger());
        var timelineRepo = new InMemoryTimelineRepository();

        var sensitiveTool = new MockTool("tool_sensitive_hotkey", "Sensitive Tool") { RiskLevel = ToolRiskLevel.HighRisk };
        toolRegistry.RegisterTool(sensitiveTool);

        var wf = new WorkflowDefinition(
            Id: "wf-hotkey-restricted",
            Name: "Restricted Hotkey Workflow",
            Description: "Workflow triggered by hotkey requiring approval",
            Category: "Security",
            Trigger: TriggerType.Hotkey,
            TriggerConfigJson: null,
            Inputs: new List<string>(),
            Actions: new List<WorkflowActionDefinition>
            {
                new("step-sens", "Sensitive Action", ActionType.ToolCall, "tool_sensitive_hotkey", "{}", true, null, 5)
            },
            TimeoutSeconds: 30,
            CompletionBehavior: CompletionBehavior.Silent,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow
        );
        await workflowRepo.CreateWorkflowAsync(wf);

        var engine = new WorkflowEngine(
            workflowRepo, toolRegistry, toolExecutor, permEngine,
            timelineRepo, null, new DenyingPromptHandler(), null);

        var hotkeyManager = new GlobalHotkeyManager();
        var executionTcs = new TaskCompletionSource<WorkflowRunRecord>();

        hotkeyManager.WorkflowHotkeyPressed += async (wfId) =>
        {
            var run = await engine.ExecuteWorkflowAsync(wfId);
            executionTcs.TrySetResult(run);
        };

        // Trigger hotkey
        hotkeyManager.TriggerWorkflowHotkey(wf.Id);

        var completedRun = await Task.WhenAny(executionTcs.Task, Task.Delay(3000));
        Assert.Same(executionTcs.Task, completedRun);

        var runResult = await executionTcs.Task;
        Assert.Equal(WorkflowExecutionStatus.Failed, runResult.Status);
        Assert.Equal(0, sensitiveTool.ExecutionCount); // PermissionEngine was not bypassed
    }
}
