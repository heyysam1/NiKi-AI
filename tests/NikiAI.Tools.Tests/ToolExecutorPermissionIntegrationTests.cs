using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NikiAI.Core.Security;
using NikiAI.Core.Tasks;
using NikiAI.Core.Tools;
using NikiAI.Security;
using NikiAI.Tools;
using Xunit;

namespace NikiAI.Tools.Tests;

public class ToolExecutorPermissionIntegrationTests
{
    private class MockPromptHandler : IApprovalPromptHandler
    {
        public Func<ApprovalRequest, Task<ApprovalDecisionResult>>? ResponseProvider { get; set; }

        public Task<ApprovalDecisionResult> RequestApprovalAsync(ApprovalRequest request, CancellationToken cancellationToken = default)
        {
            if (ResponseProvider != null)
            {
                return ResponseProvider(request);
            }
            return Task.FromResult(ApprovalDecisionResult.Denied("Mock deny"));
        }
    }

    private class MockTaskRepository : ITaskRepository
    {
        public List<TaskEvent> Events { get; } = new();

        public Task<AgentTask> CreateAsync(AgentTask task, CancellationToken cancellationToken = default) => Task.FromResult(task);
        public Task<AgentTask?> GetByIdAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult<AgentTask?>(null);
        public Task<IReadOnlyList<AgentTask>> GetAllAsync(bool includeArchived = false, int limit = 50, int offset = 0, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AgentTask>>(new List<AgentTask>());
        public Task<IReadOnlyList<AgentTask>> GetByStatusAsync(AgentTaskStatus status, bool includeArchived = false, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AgentTask>>(new List<AgentTask>());
        public Task UpdateAsync(AgentTask task, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task TransitionStatusAsync(string id, AgentTaskStatus newStatus, string? reason = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ArchiveAsync(string id, string? reason = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task PurgePermanentlyAsync(string id, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task AddEventAsync(TaskEvent taskEvent, CancellationToken cancellationToken = default)
        {
            Events.Add(taskEvent);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<TaskEvent>> GetEventsAsync(string taskId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<TaskEvent>>(Events.Where(e => e.TaskId == taskId).ToList());
        }
    }

    private class SensitiveMockTool : ITool
    {
        public string Id => "sensitive_action";
        public string Name => "Sensitive Action";
        public string Description => "Performs sensitive operation";
        public ToolRiskLevel RiskLevel => ToolRiskLevel.Sensitive;
        public string InputSchemaJson => "{}";
        public TimeSpan DefaultTimeout => TimeSpan.FromSeconds(5);
        public bool WasExecuted { get; private set; }

        public Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken = default)
        {
            WasExecuted = true;
            return Task.FromResult(ToolResult.Success(call.CallId, Id, """{"result":"ok"}""", TimeSpan.Zero));
        }
    }

    // Mandatory Correction 6: Keep permission audit events distinct from execution audit events:
    // ApprovalRequested -> ApprovalDecided -> ActionExecuted (only when execution actually occurs).
    // Do not report an action as executed when permission was denied, cancelled, or timed out.
    [Fact]
    public async Task DeniedPermission_NeverLogsActionExecuted_ToolNotExecuted()
    {
        var registry = new ToolRegistry();
        var tool = new SensitiveMockTool();
        registry.RegisterTool(tool);

        var taskRepo = new MockTaskRepository();
        var permAudit = new PermissionAuditLogger();
        var permEngine = new PermissionEngine(auditLogger: permAudit, taskRepository: taskRepo);

        var promptHandler = new MockPromptHandler
        {
            ResponseProvider = req => Task.FromResult(ApprovalDecisionResult.Denied("User denied action"))
        };
        permEngine.SetPromptHandler(promptHandler);

        var toolAudit = new ToolAuditLogger(taskRepo);
        var executor = new ToolExecutor(registry, permEngine, toolAudit);

        var call = new ToolCall("c_denied", "sensitive_action", "{}", DateTimeOffset.UtcNow);
        var result = await executor.ExecuteAsync(call, taskId: "task_test_denied");

        Assert.False(result.IsSuccess);
        Assert.False(tool.WasExecuted);

        // Verification: ActionExecuted must NEVER be logged in task events or tool audit
        Assert.DoesNotContain(taskRepo.Events, e => e.EventType == TaskEventType.ActionExecuted);
        Assert.DoesNotContain(toolAudit.GetRecentRecords(10), r => r.CallId == "c_denied");

        // Verification: Permission audit should have recorded ApprovalRequested and ApprovalDecided
        var permEvents = permAudit.GetRecentAuditRecords(10);
        Assert.Contains(permEvents, r => r.Outcome == ApprovalOutcome.Denied);
    }

    [Fact]
    public async Task ApprovedPermission_ExecutesTool_AndLogsActionExecuted()
    {
        var registry = new ToolRegistry();
        var tool = new SensitiveMockTool();
        registry.RegisterTool(tool);

        var taskRepo = new MockTaskRepository();
        var permAudit = new PermissionAuditLogger();
        var permEngine = new PermissionEngine(auditLogger: permAudit, taskRepository: taskRepo);

        var promptHandler = new MockPromptHandler
        {
            ResponseProvider = req => Task.FromResult(ApprovalDecisionResult.Approved(ApprovalDecision.AllowOnce, "Approved once"))
        };
        permEngine.SetPromptHandler(promptHandler);

        var toolAudit = new ToolAuditLogger(taskRepo);
        var executor = new ToolExecutor(registry, permEngine, toolAudit);

        var call = new ToolCall("c_approved", "sensitive_action", "{}", DateTimeOffset.UtcNow);
        var result = await executor.ExecuteAsync(call, taskId: "task_test_approved");

        Assert.True(result.IsSuccess);
        Assert.True(tool.WasExecuted);

        // Verification: ActionExecuted logged in task events and tool audit ONLY on real execution
        Assert.Contains(taskRepo.Events, e => e.EventType == TaskEventType.ActionExecuted);
        Assert.Contains(toolAudit.GetRecentRecords(10), r => r.CallId == "c_approved");

        // Verification: Permission audit recorded Approved
        var permEvents = permAudit.GetRecentAuditRecords(10);
        Assert.Contains(permEvents, r => r.Outcome == ApprovalOutcome.Approved && r.Decision == ApprovalDecision.AllowOnce);
    }
}
