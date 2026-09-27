using NikiAI.Core.Lifecycle;
using NikiAI.Core.Security;
using NikiAI.Core.Tools;
using NikiAI.Core.Workflows;
using NikiAI.Security;
using NikiAI.Tools;
using Xunit;

namespace NikiAI.Workflows.Tests;

public class WorkflowEngineTests
{
    private readonly InMemoryWorkflowRepository _workflowRepo = new();
    private readonly ToolRegistry _toolRegistry = new();
    private readonly IPermissionEngine _permEngine;
    private readonly ToolExecutor _toolExecutor;
    private readonly InMemoryTimelineRepository _timelineRepo = new();
    private readonly MockNotificationService _notifService = new();
    private readonly TaskLifecycleSignalHub _signalHub = new();

    public WorkflowEngineTests()
    {
        _permEngine = new PermissionEngine();
        _toolExecutor = new ToolExecutor(_toolRegistry, _permEngine, new FakeToolAuditLogger());
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_SequentialSteps_AllSucceed()
    {
        var tool1 = new MockTool("tool_1", "Tool 1");
        var tool2 = new MockTool("tool_2", "Tool 2");
        _toolRegistry.RegisterTool(tool1);
        _toolRegistry.RegisterTool(tool2);

        var wf = new WorkflowDefinition(
            Id: "wf-sequential-test",
            Name: "Sequential Test",
            Description: "Tests sequential step execution",
            Category: "Test",
            Trigger: TriggerType.Manual,
            TriggerConfigJson: null,
            Inputs: new List<string>(),
            Actions: new List<WorkflowActionDefinition>
            {
                new("step-1", "First Step", ActionType.ToolCall, "tool_1", "{}", false, null, 5),
                new("step-2", "Second Step", ActionType.ToolCall, "tool_2", "{}", false, null, 5),
                new("step-3", "Notify", ActionType.Notification, null, "{\"message\":\"Completed all steps\"}", false, null, 5)
            },
            TimeoutSeconds: 30,
            CompletionBehavior: CompletionBehavior.Notify,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow
        );
        await _workflowRepo.CreateWorkflowAsync(wf);

        var engine = new WorkflowEngine(
            _workflowRepo, _toolRegistry, _toolExecutor, _permEngine,
            _timelineRepo, _notifService, null, _signalHub);

        var result = await engine.ExecuteWorkflowAsync(wf.Id);

        Assert.Equal(WorkflowExecutionStatus.Completed, result.Status);
        Assert.Equal(3, result.CurrentStep);
        Assert.Equal(1, tool1.ExecutionCount);
        Assert.Equal(1, tool2.ExecutionCount);
        Assert.Single(_notifService.ShownNotifications);
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_StepFailure_StopsSubsequentSteps()
    {
        var tool1 = new MockTool("tool_failing", "Failing Tool")
        {
            ExecutionHandler = call => ToolResult.Failure(call.CallId, "tool_failing", "Operation failed", TimeSpan.FromMilliseconds(5))
        };
        var tool2 = new MockTool("tool_subsequent", "Subsequent Tool");
        _toolRegistry.RegisterTool(tool1);
        _toolRegistry.RegisterTool(tool2);

        var wf = new WorkflowDefinition(
            Id: "wf-failure-test",
            Name: "Failure Stop Test",
            Description: "Tests that failure halts the workflow",
            Category: "Test",
            Trigger: TriggerType.Manual,
            TriggerConfigJson: null,
            Inputs: new List<string>(),
            Actions: new List<WorkflowActionDefinition>
            {
                new("step-fail", "Failing Step", ActionType.ToolCall, "tool_failing", "{}", false, null, 5),
                new("step-never", "Never Step", ActionType.ToolCall, "tool_subsequent", "{}", false, null, 5)
            },
            TimeoutSeconds: 30,
            CompletionBehavior: CompletionBehavior.Silent,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow
        );
        await _workflowRepo.CreateWorkflowAsync(wf);

        var engine = new WorkflowEngine(
            _workflowRepo, _toolRegistry, _toolExecutor, _permEngine,
            _timelineRepo, _notifService, null, _signalHub);

        var result = await engine.ExecuteWorkflowAsync(wf.Id);

        Assert.Equal(WorkflowExecutionStatus.Failed, result.Status);
        Assert.Equal(1, tool1.ExecutionCount);
        Assert.Equal(0, tool2.ExecutionCount); // Proves step 2 was NEVER executed
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_TemplateSubstitution_ReplacesTokens()
    {
        string? passedArgs = null;
        var tool = new MockTool("tool_template", "Template Tool")
        {
            ExecutionHandler = call =>
            {
                passedArgs = call.ArgumentsJson;
                return ToolResult.Success(call.CallId, "tool_template", "{}", TimeSpan.FromMilliseconds(5));
            }
        };
        _toolRegistry.RegisterTool(tool);

        var wf = new WorkflowDefinition(
            Id: "wf-template-test",
            Name: "Template Test",
            Description: "Tests template token substitution",
            Category: "Test",
            Trigger: TriggerType.Manual,
            TriggerConfigJson: null,
            Inputs: new List<string> { "query" },
            Actions: new List<WorkflowActionDefinition>
            {
                new("step-tpl", "Template Step", ActionType.ToolCall, "tool_template", "{\"search\":\"{{inputs.query}}\"}", false, null, 5)
            },
            TimeoutSeconds: 30,
            CompletionBehavior: CompletionBehavior.Silent,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow
        );
        await _workflowRepo.CreateWorkflowAsync(wf);

        var engine = new WorkflowEngine(
            _workflowRepo, _toolRegistry, _toolExecutor, _permEngine,
            _timelineRepo, _notifService, null, _signalHub);

        var inputs = new Dictionary<string, string> { ["query"] = "Antigravity Agents" };
        var result = await engine.ExecuteWorkflowAsync(wf.Id, inputs);

        Assert.Equal(WorkflowExecutionStatus.Completed, result.Status);
        Assert.Equal("{\"search\":\"Antigravity Agents\"}", passedArgs);
    }
}
