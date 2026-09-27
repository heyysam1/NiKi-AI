using System.IO;
using NikiAI.Core.Security;
using NikiAI.Core.Tools;
using NikiAI.Core.Workflows;
using NikiAI.Security;
using NikiAI.Storage;
using NikiAI.Tools;
using Xunit;

namespace NikiAI.Workflows.Tests;

public class WorkflowRunPersistencePrivacyTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly StorageContext _storageContext;
    private readonly SqliteWorkflowRepository _workflowRepo;
    private readonly ToolRegistry _toolRegistry = new();
    private readonly IPermissionEngine _permEngine;
    private readonly ToolExecutor _toolExecutor;
    private readonly InMemoryTimelineRepository _timelineRepo = new();

    public WorkflowRunPersistencePrivacyTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"niki_wf_privacy_{Guid.NewGuid():N}.db");
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

    [Fact]
    public async Task WorkflowRuns_PersistsOperationalMetadataOnly_NeverSensitivePayloads()
    {
        const string secretPassword = "SuperSecretPassword123!";
        const string sensitiveInput = "SensitiveBankStatementData";

        var sensitiveTool = new MockTool("tool_sensitive", "Sensitive Tool")
        {
            ExecutionHandler = call => ToolResult.Success(call.CallId, "tool_sensitive", $"{{\"secret\":\"{secretPassword}\"}}", TimeSpan.FromMilliseconds(5))
        };
        _toolRegistry.RegisterTool(sensitiveTool);

        var wf = new WorkflowDefinition(
            Id: "wf-privacy-test",
            Name: "Privacy Test",
            Description: "Verifies sensitive payloads never enter workflow_runs",
            Category: "Test",
            Trigger: TriggerType.Manual,
            TriggerConfigJson: null,
            Inputs: new List<string> { "data" },
            Actions: new List<WorkflowActionDefinition>
            {
                new("step-sens", "Sensitive Action", ActionType.ToolCall, "tool_sensitive", $"{{\"payload\":\"{sensitiveInput}\"}}", false, null, 5)
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

        var inputs = new Dictionary<string, string> { ["data"] = sensitiveInput };
        var runResult = await engine.ExecuteWorkflowAsync(wf.Id, inputs);

        Assert.Equal(WorkflowExecutionStatus.Completed, runResult.Status);

        // Fetch persisted run record from SQLite database
        var persisted = await _workflowRepo.GetRunByIdAsync(runResult.RunId);
        Assert.NotNull(persisted);

        // Assert operational metadata is present
        Assert.Equal(runResult.RunId, persisted.RunId);
        Assert.Equal(wf.Id, persisted.WorkflowId);
        Assert.Equal(WorkflowExecutionStatus.Completed, persisted.Status);
        Assert.Equal(1, persisted.CurrentStep);
        Assert.True(persisted.DurationMs >= 0);

        // Assert strictly NO sensitive data in sanitized status info
        if (persisted.SanitizedStatusInfo != null)
        {
            Assert.DoesNotContain(secretPassword, persisted.SanitizedStatusInfo);
            Assert.DoesNotContain(sensitiveInput, persisted.SanitizedStatusInfo);
        }

        // Query raw SQLite database row to verify table schema contains NO payload or argument columns
        using var conn = _storageContext.CreateConnection();
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM workflow_runs WHERE run_id = $id;";
        cmd.Parameters.AddWithValue("$id", runResult.RunId);
        using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());

        // Check column names: must only have operational metadata columns
        var columnNames = new List<string>();
        for (int i = 0; i < reader.FieldCount; i++)
        {
            columnNames.Add(reader.GetName(i).ToLowerInvariant());
        }

        Assert.Contains("run_id", columnNames);
        Assert.Contains("workflow_id", columnNames);
        Assert.Contains("status", columnNames);
        Assert.Contains("current_step", columnNames);
        Assert.Contains("started_at", columnNames);
        Assert.Contains("completed_at", columnNames);
        Assert.Contains("duration_ms", columnNames);
        Assert.Contains("sanitized_status_info", columnNames);

        Assert.DoesNotContain("arguments", columnNames);
        Assert.DoesNotContain("results", columnNames);
        Assert.DoesNotContain("inputs", columnNames);
        Assert.DoesNotContain("outputs", columnNames);
        Assert.DoesNotContain("prompts", columnNames);
        Assert.DoesNotContain("payload", columnNames);
        Assert.DoesNotContain("secrets", columnNames);
    }
}
