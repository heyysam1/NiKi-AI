using System.Collections.Concurrent;
using System.Reflection;
using NikiAI.Agent;
using NikiAI.Core.Agent;
using NikiAI.Core.Logging;
using NikiAI.Core.Security;
using NikiAI.Core.Tasks;
using NikiAI.Core.Tools;
using NikiAI.Tools;
using Xunit;

namespace NikiAI.Agent.Tests;

public class AgentOperatorTests
{
    private class InMemoryTaskRepository : ITaskRepository
    {
        private readonly ConcurrentDictionary<string, AgentTask> _tasks = new();
        public event Action<string, AgentTaskStatus, AgentTaskStatus>? StatusChanged;

        public Task<AgentTask> CreateAsync(AgentTask task, CancellationToken cancellationToken = default)
        {
            _tasks[task.Id] = task;
            return Task.FromResult(task);
        }

        public Task<AgentTask?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
        {
            _tasks.TryGetValue(id, out var task);
            return Task.FromResult(task);
        }

        public Task<IReadOnlyList<AgentTask>> GetAllAsync(bool includeArchived = false, int limit = 100, int offset = 0, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AgentTask>>(_tasks.Values.ToList());

        public Task<IReadOnlyList<AgentTask>> GetByStatusAsync(AgentTaskStatus status, bool includeArchived = false, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AgentTask>>(_tasks.Values.Where(t => t.Status == status).ToList());

        public Task UpdateAsync(AgentTask task, CancellationToken cancellationToken = default)
        {
            _tasks[task.Id] = task;
            return Task.CompletedTask;
        }

        public Task TransitionStatusAsync(string taskId, AgentTaskStatus newStatus, string? message = null, CancellationToken cancellationToken = default)
        {
            if (_tasks.TryGetValue(taskId, out var task))
            {
                var prev = task.Status;
                task.TransitionTo(newStatus);
                StatusChanged?.Invoke(taskId, prev, newStatus);
            }
            return Task.CompletedTask;
        }

        public Task ArchiveAsync(string taskId, string? reason = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task PurgePermanentlyAsync(string taskId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task AddEventAsync(TaskEvent taskEvent, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<TaskEvent>> GetEventsAsync(string taskId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TaskEvent>>(Array.Empty<TaskEvent>());
    }

    private class TestAuditLogger : IToolAuditLogger
    {
        public List<ToolAuditRecord> Records { get; } = new();
        public Task LogExecutionAsync(ToolAuditRecord record, CancellationToken cancellationToken = default)
        {
            Records.Add(record);
            return Task.CompletedTask;
        }

        public IReadOnlyList<ToolAuditRecord> GetRecentRecords(int limit = 50) => Records.TakeLast(limit).ToList();
    }

    private class TestWeatherTool : ITool
    {
        public string Id => "get_weather";
        public string Name => "Get Weather";
        public string Description => "Get weather for a city";
        public ToolRiskLevel RiskLevel => ToolRiskLevel.Informational;
        public string InputSchemaJson => """
        {
            "type": "object",
            "required": ["city"],
            "properties": {
                "city": { "type": "string", "minLength": 2 }
            }
        }
        """;
        public TimeSpan DefaultTimeout => TimeSpan.FromSeconds(5);

        public Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ToolResult.Success(call.CallId, Id, """{"temperature": 22, "condition": "Sunny"}""", TimeSpan.FromMilliseconds(5)));
        }
    }

    private class TestWorkflowTool : ITool
    {
        public string Id => "workflow_run";
        public string Name => "Run Workflow";
        public string Description => "Runs a multi-step background workflow";
        public ToolRiskLevel RiskLevel => ToolRiskLevel.Sensitive;
        public string InputSchemaJson => """{"type": "object", "properties": {}}""";
        public TimeSpan DefaultTimeout => TimeSpan.FromSeconds(5);

        public Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ToolResult.Success(call.CallId, Id, """{"status": "Workflow executed"}""", TimeSpan.FromMilliseconds(5)));
        }
    }

    private class AutoAllowPermissionEngine : IPermissionEngine
    {
        public Task<PermissionEvaluationResult> EvaluateToolExecutionAsync(string taskId, ITool tool, ToolCall call, CancellationToken cancellationToken = default) =>
            Task.FromResult(PermissionEvaluationResult.Allowed("Auto-allowed in test"));

        public Task<ApprovalDecisionResult> RequestApprovalAsync(ApprovalRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(ApprovalDecisionResult.Approved(ApprovalDecision.AllowOnce));

        public Task<bool> RecordDecisionAsync(string scopeKey, string toolId, ApprovalDecision decision, ToolRiskLevel riskLevel, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public void RecordDecision(string toolId, ApprovalDecision decision) { }

        public Task<bool> RevokeDecisionAsync(string scopeKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public IReadOnlyDictionary<string, ApprovalDecision> GetPersistentDecisions() =>
            new Dictionary<string, ApprovalDecision>();

        public Task ReloadRulesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void SetPromptHandler(IApprovalPromptHandler promptHandler) { }
    }

    [Fact]
    public void AgentOperator_HasZeroReferencesToPermissionEngine_GuaranteedByReflection()
    {
        var operatorType = typeof(AgentOperator);

        // Verify constructor parameters
        foreach (var ctor in operatorType.GetConstructors())
        {
            foreach (var param in ctor.GetParameters())
            {
                Assert.NotEqual("IPermissionEngine", param.ParameterType.Name);
                Assert.DoesNotContain("Permission", param.ParameterType.FullName ?? "");
            }
        }

        // Verify fields
        foreach (var field in operatorType.GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
        {
            Assert.NotEqual("IPermissionEngine", field.FieldType.Name);
            Assert.DoesNotContain("Permission", field.FieldType.FullName ?? "");
        }
    }

    [Fact]
    public async Task ExecutePromptAsync_PureConversation_DoesNotCreateTaskAndReturnsResponse()
    {
        var provider = new MockAgentProvider { SimulatedLatencyMs = 0 };
        var registry = new ToolRegistry();
        var catalog = new ToolCatalog(registry);
        var permEngine = new AutoAllowPermissionEngine();
        var executor = new ToolExecutor(registry, permEngine, new TestAuditLogger());
        var taskRepo = new InMemoryTaskRepository();

        var op = new AgentOperator(provider, catalog, executor, taskRepo);

        var response = await op.ExecutePromptAsync("Hello Niki, what can you do?");

        Assert.True(response.IsSuccess);
        Assert.True(response.IsCompleted);
        Assert.Null(response.TaskId);
        Assert.Contains("Hello Niki", response.ResponseText);
        Assert.Empty(await taskRepo.GetAllAsync());
    }

    [Fact]
    public async Task ExecuteAsync_SingleToolCall_ExecutesViaToolExecutorAndCompletes()
    {
        var provider = new MockAgentProvider { SimulatedLatencyMs = 0 };
        var registry = new ToolRegistry();
        registry.RegisterTool(new TestWeatherTool());

        var catalog = new ToolCatalog(registry);
        var permEngine = new AutoAllowPermissionEngine();
        var executor = new ToolExecutor(registry, permEngine, new TestAuditLogger());
        var taskRepo = new InMemoryTaskRepository();

        int turnCount = 0;
        provider.CustomResponseHandler = req =>
        {
            turnCount++;
            if (turnCount == 1)
            {
                // First turn: model decides to call get_weather
                return new ChatCompletionResponse(
                    Content: string.Empty,
                    ToolCalls: new[] { new AgentToolCall("call_1", "get_weather", """{"city": "Tokyo"}""") }
                );
            }
            else
            {
                // Second turn: model sees weather result and generates answer
                var toolMsg = req.Messages.FirstOrDefault(m => m.Role == "tool");
                Assert.NotNull(toolMsg);
                Assert.Contains("22", toolMsg.Content);

                return new ChatCompletionResponse(
                    Content: "The weather in Tokyo is 22°C and Sunny."
                );
            }
        };

        var op = new AgentOperator(provider, catalog, executor, taskRepo);
        var response = await op.ExecuteAsync(new AgentOperatorRequest("What is Tokyo weather?", ForceDurableTracking: true));

        Assert.True(response.IsSuccess);
        Assert.True(response.IsCompleted);
        Assert.NotNull(response.TaskId);
        Assert.Equal("The weather in Tokyo is 22°C and Sunny.", response.ResponseText);

        var task = await taskRepo.GetByIdAsync(response.TaskId!);
        Assert.NotNull(task);
        Assert.Equal(AgentTaskStatus.Completed, task!.Status);
        Assert.Single(response.ExecutedToolCalls!);
    }

    [Fact]
    public async Task ExecuteAsync_ToolValidationFailure_ReturnsStructuredErrorAndLLMSelfCorrects()
    {
        var provider = new MockAgentProvider { SimulatedLatencyMs = 0 };
        var registry = new ToolRegistry();
        registry.RegisterTool(new TestWeatherTool());

        var catalog = new ToolCatalog(registry);
        var permEngine = new AutoAllowPermissionEngine();
        var executor = new ToolExecutor(registry, permEngine, new TestAuditLogger());
        var taskRepo = new InMemoryTaskRepository();

        int turnCount = 0;
        provider.CustomResponseHandler = req =>
        {
            turnCount++;
            if (turnCount == 1)
            {
                // Turn 1: LLM returns INVALID arguments (missing required "city")
                return new ChatCompletionResponse(
                    Content: string.Empty,
                    ToolCalls: new[] { new AgentToolCall("call_err", "get_weather", """{"invalid_field": 123}""") }
                );
            }
            else if (turnCount == 2)
            {
                // Turn 2: LLM inspects validation error from ToolExecutor and self-corrects!
                var toolMsg = req.Messages.LastOrDefault(m => m.Role == "tool");
                Assert.NotNull(toolMsg);
                Assert.Contains("validation error", toolMsg!.Content, StringComparison.OrdinalIgnoreCase);

                return new ChatCompletionResponse(
                    Content: string.Empty,
                    ToolCalls: new[] { new AgentToolCall("call_ok", "get_weather", """{"city": "Paris"}""") }
                );
            }
            else
            {
                // Turn 3: LLM provides final answer after corrected tool call succeeded
                return new ChatCompletionResponse(
                    Content: "The weather in Paris is 22°C and Sunny."
                );
            }
        };

        var op = new AgentOperator(provider, catalog, executor, taskRepo);
        var response = await op.ExecuteAsync(new AgentOperatorRequest("Weather please", ForceDurableTracking: true));

        Assert.True(response.IsSuccess);
        Assert.True(response.IsCompleted);
        Assert.Equal(3, turnCount);
        Assert.Equal("The weather in Paris is 22°C and Sunny.", response.ResponseText);
        Assert.Equal(2, response.ExecutedToolCalls!.Count);
    }

    [Fact]
    public async Task ExecuteAsync_MaxTurnsExhausted_TransitionsTaskToFailedNeverCompleted()
    {
        var provider = new MockAgentProvider { SimulatedLatencyMs = 0 };
        var registry = new ToolRegistry();
        registry.RegisterTool(new TestWeatherTool());

        var catalog = new ToolCatalog(registry);
        var permEngine = new AutoAllowPermissionEngine();
        var executor = new ToolExecutor(registry, permEngine, new TestAuditLogger());
        var taskRepo = new InMemoryTaskRepository();

        // Loop infinitely requesting tools
        provider.CustomResponseHandler = req => new ChatCompletionResponse(
            Content: string.Empty,
            ToolCalls: new[] { new AgentToolCall("call_infinite", "get_weather", """{"city": "LoopCity"}""") }
        );

        var op = new AgentOperator(provider, catalog, executor, taskRepo)
        {
            MaxTurns = 3 // small limit for test
        };

        var response = await op.ExecuteAsync(new AgentOperatorRequest("Infinite tool loop", ForceDurableTracking: true));

        Assert.False(response.IsSuccess);
        Assert.False(response.IsCompleted);
        Assert.Contains("Maximum execution turns", response.ErrorMessage);

        var task = await taskRepo.GetByIdAsync(response.TaskId!);
        Assert.NotNull(task);
        // CRITICAL CHECK: Max-turn exhaustion must become Failed, never Completed!
        Assert.Equal(AgentTaskStatus.Failed, task!.Status);
    }

    [Fact]
    public async Task ExecuteAsync_RequiresDurableTool_DynamicallyPromotesTransientToDurableTask()
    {
        var provider = new MockAgentProvider { SimulatedLatencyMs = 0 };
        var registry = new ToolRegistry();
        registry.RegisterTool(new TestWorkflowTool()); // Marked as durable in ToolCatalog

        var catalog = new ToolCatalog(registry);
        var permEngine = new AutoAllowPermissionEngine();
        var executor = new ToolExecutor(registry, permEngine, new TestAuditLogger());
        var taskRepo = new InMemoryTaskRepository();

        int turnCount = 0;
        provider.CustomResponseHandler = req =>
        {
            turnCount++;
            if (turnCount == 1)
            {
                return new ChatCompletionResponse(
                    Content: string.Empty,
                    ToolCalls: new[] { new AgentToolCall("call_wf", "workflow_run", "{}") }
                );
            }
            else
            {
                return new ChatCompletionResponse(Content: "Workflow finished!");
            }
        };

        var op = new AgentOperator(provider, catalog, executor, taskRepo);

        // Start with ForceDurableTracking = FALSE (casual/transient prompt)
        var response = await op.ExecuteAsync(new AgentOperatorRequest("Run background process", ForceDurableTracking: false));

        Assert.True(response.IsSuccess);
        Assert.True(response.IsCompleted);
        // CRITICAL CHECK: Dynamically promoted to durable task because tool requires durable persistence!
        Assert.NotNull(response.TaskId);

        var task = await taskRepo.GetByIdAsync(response.TaskId!);
        Assert.NotNull(task);
        Assert.Equal(AgentTaskStatus.Completed, task!.Status);
    }

    [Fact]
    public void AgentOperator_DefaultMaxTurns_IsEight()
    {
        var provider = new MockAgentProvider();
        var registry = new ToolRegistry();
        var catalog = new ToolCatalog(registry);
        var permEngine = new AutoAllowPermissionEngine();
        var executor = new ToolExecutor(registry, permEngine, new TestAuditLogger());
        var taskRepo = new InMemoryTaskRepository();

        var op = new AgentOperator(provider, catalog, executor, taskRepo);

        Assert.Equal(8, op.MaxTurns);
        Assert.Equal(8, AgentOperator.DefaultMaxTurns);
    }

    [Fact]
    public async Task ExecuteAsync_EightTurnExhaustion_TransitionsTaskToFailedNeverCompleted()
    {
        var provider = new MockAgentProvider { SimulatedLatencyMs = 0 };
        var registry = new ToolRegistry();
        registry.RegisterTool(new TestWeatherTool());

        var catalog = new ToolCatalog(registry);
        var permEngine = new AutoAllowPermissionEngine();
        var executor = new ToolExecutor(registry, permEngine, new TestAuditLogger());
        var taskRepo = new InMemoryTaskRepository();

        int callCount = 0;
        provider.CustomResponseHandler = req =>
        {
            callCount++;
            return new ChatCompletionResponse(
                Content: string.Empty,
                ToolCalls: new[] { new AgentToolCall($"call_{callCount}", "get_weather", """{"city": "LoopCity"}""") }
            );
        };

        var op = new AgentOperator(provider, catalog, executor, taskRepo);

        var response = await op.ExecuteAsync(new AgentOperatorRequest("Infinite tool loop", ForceDurableTracking: true));

        Assert.False(response.IsSuccess);
        Assert.False(response.IsCompleted);
        Assert.Contains("Maximum execution turns (8) reached", response.ErrorMessage);
        Assert.Equal(8, callCount);

        var task = await taskRepo.GetByIdAsync(response.TaskId!);
        Assert.NotNull(task);
        Assert.Equal(AgentTaskStatus.Failed, task!.Status);
    }

    [Fact]
    public async Task ExecuteAsync_UnrecoveredToolFailure_TransitionsToFailedNeverCompleted()
    {
        var provider = new MockAgentProvider { SimulatedLatencyMs = 0 };
        var registry = new ToolRegistry();
        registry.RegisterTool(new TestWeatherTool());

        var catalog = new ToolCatalog(registry);
        var permEngine = new AutoAllowPermissionEngine();
        var executor = new ToolExecutor(registry, permEngine, new TestAuditLogger());
        var taskRepo = new InMemoryTaskRepository();

        int turnCount = 0;
        provider.CustomResponseHandler = req =>
        {
            turnCount++;
            if (turnCount == 1)
            {
                // Turn 1: LLM returns invalid arguments -> tool failure
                return new ChatCompletionResponse(
                    Content: string.Empty,
                    ToolCalls: new[] { new AgentToolCall("call_err", "get_weather", """{"invalid_field": 123}""") }
                );
            }
            else
            {
                // Turn 2: LLM produces text instead of correcting the tool call
                return new ChatCompletionResponse(
                    Content: "Sorry, I couldn't get the weather."
                );
            }
        };

        var op = new AgentOperator(provider, catalog, executor, taskRepo);
        var response = await op.ExecuteAsync(new AgentOperatorRequest("Weather please", ForceDurableTracking: true));

        // The durable task must NOT be marked Completed when there was an unrecovered tool failure!
        Assert.False(response.IsSuccess);
        Assert.False(response.IsCompleted);
        Assert.NotNull(response.TaskId);
        Assert.Contains("unresolved tool failure", response.ErrorMessage ?? "");

        var task = await taskRepo.GetByIdAsync(response.TaskId!);
        Assert.NotNull(task);
        Assert.Equal(AgentTaskStatus.Failed, task!.Status);
    }

    private class TestStatusTransitionTool : ITool
    {
        private readonly InMemoryTaskRepository _taskRepo;
        private readonly AgentTaskStatus _targetStatus;

        public TestStatusTransitionTool(InMemoryTaskRepository taskRepo, AgentTaskStatus targetStatus)
        {
            _taskRepo = taskRepo;
            _targetStatus = targetStatus;
        }

        public string Id => "pause_task";
        public string Name => "Pause Task";
        public string Description => "Transitions task to target status";
        public ToolRiskLevel RiskLevel => ToolRiskLevel.Sensitive;
        public string InputSchemaJson => """{"type": "object", "properties": {}}""";
        public TimeSpan DefaultTimeout => TimeSpan.FromSeconds(5);

        public async Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken = default)
        {
            var all = await _taskRepo.GetAllAsync(cancellationToken: cancellationToken);
            var running = all.FirstOrDefault(t => t.Status == AgentTaskStatus.Running);
            if (running is not null)
            {
                await _taskRepo.TransitionStatusAsync(running.Id, _targetStatus, "Paused by tool", cancellationToken);
            }
            return ToolResult.Success(call.CallId, Id, """{"status": "paused"}""", TimeSpan.FromMilliseconds(5));
        }
    }

    [Theory]
    [InlineData(AgentTaskStatus.Waiting)]
    [InlineData(AgentTaskStatus.NeedsApproval)]
    public async Task ExecuteAsync_TaskInWaitingOrNeedsApproval_ReturnsIsCompletedFalse(AgentTaskStatus waitingStatus)
    {
        var provider = new MockAgentProvider { SimulatedLatencyMs = 0 };
        var registry = new ToolRegistry();
        var taskRepo = new InMemoryTaskRepository();
        registry.RegisterTool(new TestStatusTransitionTool(taskRepo, waitingStatus));

        var catalog = new ToolCatalog(registry);
        var permEngine = new AutoAllowPermissionEngine();
        var executor = new ToolExecutor(registry, permEngine, new TestAuditLogger());

        int turnCount = 0;
        provider.CustomResponseHandler = req =>
        {
            turnCount++;
            if (turnCount == 1)
            {
                return new ChatCompletionResponse(
                    Content: string.Empty,
                    ToolCalls: new[] { new AgentToolCall("call_pause", "pause_task", "{}") }
                );
            }
            else
            {
                return new ChatCompletionResponse(Content: "Awaiting your action.");
            }
        };

        var op = new AgentOperator(provider, catalog, executor, taskRepo);
        var response = await op.ExecuteAsync(new AgentOperatorRequest("Start pause flow", ForceDurableTracking: true));

        Assert.False(response.IsCompleted);
        Assert.NotNull(response.TaskId);

        var task = await taskRepo.GetByIdAsync(response.TaskId!);
        Assert.NotNull(task);
        Assert.Equal(waitingStatus, task!.Status);
    }
}
