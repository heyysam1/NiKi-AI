using System.Collections.Concurrent;
using NikiAI.Core.Security;
using NikiAI.Core.Tasks;
using NikiAI.Core.Tools;
using NikiAI.Security;
using NikiAI.Tools;
using Xunit;

namespace NikiAI.Tools.Tests;

public class ToolExecutorTests
{
    private class FakeTool : ITool
    {
        public string Id { get; set; } = "fake_tool";
        public string Name { get; set; } = "Fake Tool";
        public string Description { get; set; } = "A fake test tool";
        public ToolRiskLevel RiskLevel { get; set; } = ToolRiskLevel.LowRiskReversible;
        public string InputSchemaJson { get; set; } = """
        {
          "type": "object",
          "required": ["input"],
          "properties": {
            "input": { "type": "string" }
          }
        }
        """;
        public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(5);
        public TimeSpan Delay { get; set; } = TimeSpan.Zero;
        public bool ThrowCancellationOnToken { get; set; } = true;
        public int InvocationCount { get; private set; }

        public async Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            if (Delay > TimeSpan.Zero)
            {
                await Task.Delay(Delay, cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return ToolResult.Success(call.CallId, Id, """{"result": "success"}""", TimeSpan.FromMilliseconds(10));
        }
    }

    private class MockTaskRepository : ITaskRepository
    {
        public ConcurrentBag<TaskEvent> Events { get; } = new();

        public Task AddEventAsync(TaskEvent taskEvent, CancellationToken cancellationToken = default)
        {
            Events.Add(taskEvent);
            return Task.CompletedTask;
        }

        public Task<AgentTask> CreateAsync(AgentTask task, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<AgentTask?> GetByIdAsync(string id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<AgentTask>> GetAllAsync(bool includeArchived = false, int limit = 100, int offset = 0, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<AgentTask>> GetByStatusAsync(AgentTaskStatus status, bool includeArchived = false, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task UpdateAsync(AgentTask task, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task TransitionStatusAsync(string taskId, AgentTaskStatus newStatus, string? message = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task ArchiveAsync(string taskId, string? reason = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task PurgePermanentlyAsync(string taskId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<TaskEvent>> GetEventsAsync(string taskId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TaskEvent>>(Events.Where(e => e.TaskId == taskId).ToList());
    }

    [Fact]
    public async Task ExecuteAsync_ValidToolCall_ExecutesSuccessfullyAndAudits()
    {
        var registry = new ToolRegistry();
        var tool = new FakeTool();
        registry.RegisterTool(tool);

        var taskRepo = new MockTaskRepository();
        var auditLogger = new ToolAuditLogger(taskRepo);
        var permEngine = new PermissionEngine();
        var executor = new ToolExecutor(registry, permEngine, auditLogger);

        var call = new ToolCall("c1", "fake_tool", """{ "input": "test-data" }""", DateTimeOffset.UtcNow);
        var result = await executor.ExecuteAsync(call, taskId: "task_123");

        Assert.True(result.IsSuccess);
        Assert.Equal(1, tool.InvocationCount);

        // Verify audit event persisted in task repository
        Assert.Single(taskRepo.Events);
        var evt = taskRepo.Events.First();
        Assert.Equal("task_123", evt.TaskId);
        Assert.Equal(TaskEventType.ActionExecuted, evt.EventType);
        Assert.Contains("fake_tool", evt.Message);
    }

    [Fact]
    public async Task ExecuteAsync_StandaloneContext_RecordsAuditWithoutTaskRepositoryCrash()
    {
        var registry = new ToolRegistry();
        var tool = new FakeTool();
        registry.RegisterTool(tool);

        var auditLogger = new ToolAuditLogger(taskRepository: null);
        var permEngine = new PermissionEngine();
        var executor = new ToolExecutor(registry, permEngine, auditLogger);

        var call = new ToolCall("c2", "fake_tool", """{ "input": "standalone-data" }""", DateTimeOffset.UtcNow);
        var result = await executor.ExecuteAsync(call, taskId: null); // Standalone: taskId is null!

        Assert.True(result.IsSuccess);
        var recent = auditLogger.GetRecentRecords();
        Assert.NotEmpty(recent);
        Assert.Null(recent[0].TaskId);
    }

    [Fact]
    public async Task ExecuteAsync_UnknownTool_ReturnsFailure()
    {
        var registry = new ToolRegistry();
        var auditLogger = new ToolAuditLogger();
        var permEngine = new PermissionEngine();
        var executor = new ToolExecutor(registry, permEngine, auditLogger);

        var call = new ToolCall("c3", "non_existent_tool", "{}", DateTimeOffset.UtcNow);
        var result = await executor.ExecuteAsync(call);

        Assert.False(result.IsSuccess);
        Assert.Contains("not found in registry", result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_SchemaValidationFailure_AbortsBeforeExecution()
    {
        var registry = new ToolRegistry();
        var tool = new FakeTool();
        registry.RegisterTool(tool);

        var auditLogger = new ToolAuditLogger();
        var permEngine = new PermissionEngine();
        var executor = new ToolExecutor(registry, permEngine, auditLogger);

        // Missing required property "input"
        var call = new ToolCall("c4", "fake_tool", "{}", DateTimeOffset.UtcNow);
        var result = await executor.ExecuteAsync(call);

        Assert.False(result.IsSuccess);
        Assert.Contains("Input validation error", result.ErrorMessage);
        Assert.Equal(0, tool.InvocationCount); // Tool was never executed!
    }

    [Fact]
    public async Task ExecuteAsync_PermissionDenied_AbortsBeforeExecution()
    {
        var registry = new ToolRegistry();
        var tool = new FakeTool();
        registry.RegisterTool(tool);

        var auditLogger = new ToolAuditLogger();
        var permEngine = new PermissionEngine();
        permEngine.RecordDecision(tool.Id, ApprovalDecision.Deny); // Explicitly denied!

        var executor = new ToolExecutor(registry, permEngine, auditLogger);

        var call = new ToolCall("c5", "fake_tool", """{ "input": "test" }""", DateTimeOffset.UtcNow);
        var result = await executor.ExecuteAsync(call);

        Assert.False(result.IsSuccess);
        Assert.Contains("Permission Denied", result.ErrorMessage);
        Assert.Equal(0, tool.InvocationCount); // Tool was never executed!
    }

    [Fact]
    public async Task ExecuteAsync_ToolExceedingTimeout_FailsWithTimeout()
    {
        var registry = new ToolRegistry();
        var tool = new FakeTool
        {
            DefaultTimeout = TimeSpan.FromMilliseconds(50),
            Delay = TimeSpan.FromMilliseconds(500) // Delay is longer than timeout
        };
        registry.RegisterTool(tool);

        var auditLogger = new ToolAuditLogger();
        var permEngine = new PermissionEngine();
        var executor = new ToolExecutor(registry, permEngine, auditLogger);

        var call = new ToolCall("c6", "fake_tool", """{ "input": "timeout-test" }""", DateTimeOffset.UtcNow);
        var result = await executor.ExecuteAsync(call);

        Assert.False(result.IsSuccess);
        Assert.Contains("timed out", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_UserCancellation_AbortsWithCancellation()
    {
        var registry = new ToolRegistry();
        var tool = new FakeTool
        {
            Delay = TimeSpan.FromMilliseconds(500)
        };
        registry.RegisterTool(tool);

        var auditLogger = new ToolAuditLogger();
        var permEngine = new PermissionEngine();
        var executor = new ToolExecutor(registry, permEngine, auditLogger);

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Pre-cancelled token

        var call = new ToolCall("c7", "fake_tool", """{ "input": "cancel-test" }""", DateTimeOffset.UtcNow);
        var result = await executor.ExecuteAsync(call, cancellationToken: cts.Token);

        Assert.False(result.IsSuccess);
        Assert.Contains("cancelled", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_SecretInArguments_IsRedactedInAuditLog()
    {
        var registry = new ToolRegistry();
        var tool = new FakeTool();
        registry.RegisterTool(tool);

        var auditLogger = new ToolAuditLogger();
        var permEngine = new PermissionEngine();
        var executor = new ToolExecutor(registry, permEngine, auditLogger);

        // Argument containing OpenAI API key
        var call = new ToolCall("c8", "fake_tool", """{ "input": "Bearer sk-1234567890abcdef1234567890abcdef" }""", DateTimeOffset.UtcNow);
        var result = await executor.ExecuteAsync(call);

        Assert.True(result.IsSuccess);
        var recent = auditLogger.GetRecentRecords();
        Assert.NotEmpty(recent);

        var sanitizedArgs = recent[0].SanitizedArgumentsJson;
        Assert.DoesNotContain("sk-1234567890abcdef1234567890abcdef", sanitizedArgs);
        Assert.Contains("[REDACTED]", sanitizedArgs);
    }
}
