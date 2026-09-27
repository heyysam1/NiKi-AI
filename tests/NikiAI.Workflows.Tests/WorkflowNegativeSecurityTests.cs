using System.IO;
using NikiAI.Core.Character;
using NikiAI.Core.Memory;
using NikiAI.Core.Security;
using NikiAI.Core.Tools;
using NikiAI.Core.Workflows;
using NikiAI.Memory;
using NikiAI.Security;
using NikiAI.Storage;
using NikiAI.Tools;
using Xunit;

namespace NikiAI.Workflows.Tests;

public class WorkflowNegativeSecurityTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly StorageContext _storageContext;
    private readonly SqliteWorkflowRepository _workflowRepo;
    private readonly ToolRegistry _toolRegistry = new();
    private readonly IPermissionEngine _permEngine;
    private readonly ToolExecutor _toolExecutor;
    private readonly InMemoryTimelineRepository _timelineRepo = new();

    public WorkflowNegativeSecurityTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"niki_wf_neg_{Guid.NewGuid():N}.db");
        _storageContext = new StorageContext(_testDbPath);
        _storageContext.InitializeAsync().GetAwaiter().GetResult();
        _workflowRepo = new SqliteWorkflowRepository(_storageContext);

        _permEngine = new PermissionEngine();
        _toolExecutor = new ToolExecutor(_toolRegistry, _permEngine, new FakeToolAuditLogger());
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_testDbPath)) File.Delete(_testDbPath);
        }
        catch { }
    }

    private class DenyingPromptHandler : IApprovalPromptHandler
    {
        public Task<ApprovalDecisionResult> RequestApprovalAsync(ApprovalRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ApprovalDecisionResult.Denied("User clicked Deny"));
        }
    }

    [Fact]
    public async Task PermissionDenial_HaltsExecution_AndBlocksAction()
    {
        var sensitiveTool = new MockTool("tool_sensitive_deny", "Restricted Action");
        _toolRegistry.RegisterTool(sensitiveTool);

        var wf = new WorkflowDefinition(
            Id: "wf-deny-test",
            Name: "Denial Test Workflow",
            Description: "Verifies workflow halts when approval is denied",
            Category: "Security",
            Trigger: TriggerType.Manual,
            TriggerConfigJson: null,
            Inputs: new List<string>(),
            Actions: new List<WorkflowActionDefinition>
            {
                new("step-deny", "Denied Step", ActionType.ToolCall, "tool_sensitive_deny", "{}", true, null, 5),
                new("step-unreached", "Unreached Step", ActionType.ToolCall, "tool_sensitive_deny", "{}", false, null, 5)
            },
            TimeoutSeconds: 30,
            CompletionBehavior: CompletionBehavior.Silent,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow
        );
        await _workflowRepo.CreateWorkflowAsync(wf);

        var engine = new WorkflowEngine(
            _workflowRepo, _toolRegistry, _toolExecutor, _permEngine,
            _timelineRepo, null, new DenyingPromptHandler(), null);

        var result = await engine.ExecuteWorkflowAsync(wf.Id);

        Assert.Equal(WorkflowExecutionStatus.Failed, result.Status);
        Assert.Equal(0, sensitiveTool.ExecutionCount); // Zero execution calls
    }

    [Fact]
    public async Task WorkflowExecution_DoesNotWriteToMemoryItems()
    {
        var mockTool = new MockTool("tool_ordinary");
        _toolRegistry.RegisterTool(mockTool);

        var wf = new WorkflowDefinition(
            Id: "wf-memory-isolation-test",
            Name: "Memory Isolation Test",
            Description: "Proves workflow does not silently write memory items",
            Category: "Security",
            Trigger: TriggerType.Manual,
            TriggerConfigJson: null,
            Inputs: new List<string>(),
            Actions: new List<WorkflowActionDefinition>
            {
                new("step-ord", "Ordinary Step", ActionType.ToolCall, "tool_ordinary", "{}", false, null, 5)
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

        await engine.ExecuteWorkflowAsync(wf.Id);

        // Verify SQLite memory_items table remains completely empty
        using var conn = _storageContext.CreateConnection();
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM memory_items;";
        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task PromptAgent_CannotModifyWorkflowActionGraph_OrBypassSubsequentPermissionChecks()
    {
        var safeTool = new MockTool("tool_step1", "Safe Tool");
        var restrictedTool = new MockTool("tool_step3", "Restricted Tool") { RiskLevel = ToolRiskLevel.HighRisk };
        _toolRegistry.RegisterTool(safeTool);
        _toolRegistry.RegisterTool(restrictedTool);

        var initialActions = new List<WorkflowActionDefinition>
        {
            new("step-1", "Pre-Agent Step", ActionType.ToolCall, "tool_step1", "{}", false, null, 5),
            new("step-2", "Prompt Agent Step", ActionType.PromptAgent, null, "{\"prompt\":\"Do some task\"}", false, null, 5),
            new("step-3", "Post-Agent Step", ActionType.ToolCall, "tool_step3", "{}", true, null, 5)
        };

        var wf = new WorkflowDefinition(
            Id: "wf-promptagent-boundary-test",
            Name: "PromptAgent Boundary Test",
            Description: "Verifies PromptAgent cannot mutate action graph or bypass permissions",
            Category: "Security",
            Trigger: TriggerType.Manual,
            TriggerConfigJson: null,
            Inputs: new List<string>(),
            Actions: initialActions,
            TimeoutSeconds: 30,
            CompletionBehavior: CompletionBehavior.Silent,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow
        );
        await _workflowRepo.CreateWorkflowAsync(wf);

        var engine = new WorkflowEngine(
            _workflowRepo, _toolRegistry, _toolExecutor, _permEngine,
            _timelineRepo, null, new DenyingPromptHandler(), null);

        var result = await engine.ExecuteWorkflowAsync(wf.Id);

        // 1. Step 1 executed successfully
        Assert.Equal(1, safeTool.ExecutionCount);

        // 2. Step 3 approval check was NOT bypassed: execution halted with failure, and restricted tool was NOT executed
        Assert.Equal(WorkflowExecutionStatus.Failed, result.Status);
        Assert.Equal(0, restrictedTool.ExecutionCount);

        // 3. Verify workflow definition in repository was NOT mutated (no actions added, removed, or reordered)
        var persistedWf = await _workflowRepo.GetWorkflowByIdAsync(wf.Id);
        Assert.NotNull(persistedWf);
        Assert.Equal(3, persistedWf.Actions.Count);
        Assert.Equal("step-1", persistedWf.Actions[0].Id);
        Assert.Equal(ActionType.ToolCall, persistedWf.Actions[0].ActionType);
        Assert.Equal("step-2", persistedWf.Actions[1].Id);
        Assert.Equal(ActionType.PromptAgent, persistedWf.Actions[1].ActionType);
        Assert.Equal("step-3", persistedWf.Actions[2].Id);
        Assert.Equal(ActionType.ToolCall, persistedWf.Actions[2].ActionType);
        Assert.True(persistedWf.Actions[2].RequiresApproval);
    }

    [Fact]
    public async Task WorkflowExecution_DoesNotModifyPetContextSnapshot()
    {
        var mockTool = new MockTool("tool_pet_context_test");
        _toolRegistry.RegisterTool(mockTool);

        var petContextProvider = new DesktopPetContextProvider();
        var baselineSnapshot = petContextProvider.GetContextSnapshot();

        var wf = new WorkflowDefinition(
            Id: "wf-petcontext-isolation-test",
            Name: "PetContext Isolation Test",
            Description: "Workflow with secret inputs and sensitive notifications",
            Category: "Security",
            Trigger: TriggerType.Manual,
            TriggerConfigJson: null,
            Inputs: new List<string> { "secret_key" },
            Actions: new List<WorkflowActionDefinition>
            {
                new("step-tool", "Tool Step", ActionType.ToolCall, "tool_pet_context_test", "{}", false, null, 5),
                new("step-notif", "Notify Step", ActionType.Notification, null, "{\"message\":\"Sensitive Secret Message 123\"}", false, null, 5)
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

        var inputs = new Dictionary<string, string> { ["secret_key"] = "SuperSecretTokenABC" };
        var result = await engine.ExecuteWorkflowAsync(wf.Id, inputs);

        Assert.Equal(WorkflowExecutionStatus.Completed, result.Status);

        // Retrieve PetContextSnapshot after workflow execution
        var afterSnapshot = petContextProvider.GetContextSnapshot();

        // 1. Verify PetContext does not leak or store workflow secrets or text
        var snapshotJson = System.Text.Json.JsonSerializer.Serialize(afterSnapshot);
        Assert.DoesNotContain("SuperSecretTokenABC", snapshotJson);
        Assert.DoesNotContain("Sensitive Secret Message 123", snapshotJson);
        Assert.DoesNotContain("wf-petcontext-isolation-test", snapshotJson);

        // 2. Verify active task status remains unaffected (null / unchanged)
        Assert.Equal(baselineSnapshot.CurrentTaskState, afterSnapshot.CurrentTaskState);
    }
}
