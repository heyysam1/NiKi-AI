using Microsoft.Extensions.Logging;
using NikiAI.Core.Agent;
using NikiAI.Core.Tasks;
using NikiAI.Core.Tools;
using NikiAI.Tools;

namespace NikiAI.Agent;

/// <summary>
/// Orchestrates agent execution loops with native LLM function/tool calling.
/// Enforces:
/// - Sole security/permission authority belongs to IToolExecutor (AgentOperator has ZERO PermissionEngine references).
/// - Structured tool results including validation failures are returned to LLM for self-correction.
/// - Transient-by-default execution, dynamically promoting to durable tracking when required.
/// - Strict canonical task lifecycle transitions (Draft -> Pending -> Running -> Completed/Failed).
/// - No false completion: max turn exhaustion transitions to Failed, never Completed.
/// </summary>
public class AgentOperator : IAgentOperator
{
    public const int DefaultMaxTurns = 8;
    private readonly IAgentProvider _agentProvider;
    private readonly IToolCatalog _toolCatalog;
    private readonly IToolExecutor _toolExecutor;
    private readonly ITaskRepository? _taskRepository;
    private readonly NikiAI.Core.Memory.IMemoryService? _memoryService;
    private readonly ILogger<AgentOperator>? _logger;

    public int MaxTurns { get; set; } = DefaultMaxTurns;

    public AgentOperator(
        IAgentProvider agentProvider,
        IToolCatalog toolCatalog,
        IToolExecutor toolExecutor,
        ITaskRepository? taskRepository = null,
        NikiAI.Core.Memory.IMemoryService? memoryService = null,
        ILogger<AgentOperator>? logger = null)
    {
        _agentProvider = agentProvider ?? throw new ArgumentNullException(nameof(agentProvider));
        _toolCatalog = toolCatalog ?? throw new ArgumentNullException(nameof(toolCatalog));
        _toolExecutor = toolExecutor ?? throw new ArgumentNullException(nameof(toolExecutor));
        _taskRepository = taskRepository;
        _memoryService = memoryService;
        _logger = logger;
    }

    public Task<AgentOperatorResponse> ExecutePromptAsync(
        string prompt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        return ExecuteAsync(new AgentOperatorRequest(prompt), cancellationToken);
    }

    public async Task<AgentOperatorResponse> ExecuteAsync(
        AgentOperatorRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string? currentTaskId = request.TaskId;
        AgentTask? currentTask = null;

        // 1. Initialize Task Tracking (if durable)
        if (!string.IsNullOrWhiteSpace(currentTaskId) && _taskRepository != null)
        {
            currentTask = await _taskRepository.GetByIdAsync(currentTaskId, cancellationToken);
            if (currentTask != null)
            {
                if (currentTask.Status == AgentTaskStatus.Draft)
                {
                    await _taskRepository.TransitionStatusAsync(currentTaskId, AgentTaskStatus.Pending, "Task queued for execution.", cancellationToken);
                    await _taskRepository.TransitionStatusAsync(currentTaskId, AgentTaskStatus.Running, "AgentOperator started execution.", cancellationToken);
                }
                else if (currentTask.Status == AgentTaskStatus.Pending)
                {
                    await _taskRepository.TransitionStatusAsync(currentTaskId, AgentTaskStatus.Running, "AgentOperator started execution.", cancellationToken);
                }
            }
        }
        else if (request.ForceDurableTracking && _taskRepository != null)
        {
            currentTaskId = await CreateAndStartDurableTaskAsync(request.UserPrompt, cancellationToken);
            currentTask = await _taskRepository.GetByIdAsync(currentTaskId, cancellationToken);
        }

        // 2. Prepare Context Messages
        var messages = new List<AgentMessage>();

        // System instructions
        string systemPrompt = request.SystemPromptOverride ??
            "You are Niki AI, a precise, helpful, and secure desktop AI assistant. " +
            "You can use available tools when needed. If a tool returns a validation or execution error, " +
            "inspect the error and attempt to correct your arguments in a subsequent call.";

        if (_memoryService != null && _memoryService.IsMemoryEnabled())
        {
            try
            {
                var longTermMemories = await _memoryService.GetMemoriesAsync(NikiAI.Core.Memory.MemoryCategory.LongTerm, cancellationToken);
                if (longTermMemories != null && longTermMemories.Count > 0)
                {
                    var memBuilder = new System.Text.StringBuilder();
                    memBuilder.AppendLine();
                    memBuilder.AppendLine("<user_memory_context>");
                    memBuilder.AppendLine("The user has explicitly saved the following facts and preferences:");
                    foreach (var m in longTermMemories)
                    {
                        memBuilder.AppendLine($"- {m.Content}");
                    }
                    memBuilder.Append("</user_memory_context>");
                    systemPrompt += memBuilder.ToString();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Failed to load user memories into system context.");
            }
        }

        messages.Add(AgentMessage.System(systemPrompt));

        // Conversation history (if provided)
        if (request.ConversationHistory != null && request.ConversationHistory.Count > 0)
        {
            foreach (var histMsg in request.ConversationHistory)
            {
                if (!string.Equals(histMsg.Role, "system", StringComparison.OrdinalIgnoreCase))
                {
                    messages.Add(histMsg);
                }
            }
        }

        // User Prompt
        messages.Add(AgentMessage.User(request.UserPrompt));

        var executedToolCalls = new List<AgentToolCall>();
        var toolResults = new List<AgentToolResult>();
        var toolDefinitions = _toolCatalog.GetToolDefinitions();

        // 3. Multi-Turn Operator Loop
        for (int turn = 0; turn < MaxTurns; turn++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                if (!string.IsNullOrWhiteSpace(currentTaskId) && _taskRepository != null)
                {
                    try
                    {
                        await _taskRepository.TransitionStatusAsync(currentTaskId, AgentTaskStatus.Cancelled, "Execution cancelled by user.", CancellationToken.None);
                    }
                    catch { }
                }
                return new AgentOperatorResponse("Execution was cancelled.", currentTaskId, IsSuccess: false, IsCompleted: false, executedToolCalls, "Operation cancelled.");
            }

            var completionRequest = new ChatCompletionRequest(
                Messages: messages,
                Tools: toolDefinitions.Count > 0 ? toolDefinitions : null,
                Temperature: 0.2);

            ChatCompletionResponse completionResponse;
            try
            {
                completionResponse = await _agentProvider.GenerateResponseAsync(completionRequest, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Provider call failed on turn {Turn}", turn);
                if (!string.IsNullOrWhiteSpace(currentTaskId) && _taskRepository != null)
                {
                    try
                    {
                        await _taskRepository.TransitionStatusAsync(currentTaskId, AgentTaskStatus.Failed, $"LLM provider error: {ex.Message}", CancellationToken.None);
                    }
                    catch { }
                }
                return new AgentOperatorResponse($"Provider error: {ex.Message}", currentTaskId, IsSuccess: false, IsCompleted: false, executedToolCalls, ex.Message);
            }

            // Case A: LLM requested Tool Calls
            if (completionResponse.HasToolCalls && completionResponse.ToolCalls != null && completionResponse.ToolCalls.Count > 0)
            {
                // Dynamic promotion to durable task if required
                if (string.IsNullOrWhiteSpace(currentTaskId) && _taskRepository != null)
                {
                    bool requiresDurable = completionResponse.ToolCalls.Any(tc => _toolCatalog.RequiresDurablePersistence(tc.ToolName));
                    if (requiresDurable)
                    {
                        currentTaskId = await CreateAndStartDurableTaskAsync(request.UserPrompt, cancellationToken);
                        currentTask = await _taskRepository.GetByIdAsync(currentTaskId, cancellationToken);
                    }
                }

                // Add assistant message containing the tool calls to conversation
                messages.Add(AgentMessage.Assistant(completionResponse.Content ?? string.Empty, completionResponse.ToolCalls));

                // Execute each tool call through IToolExecutor
                foreach (var toolCall in completionResponse.ToolCalls)
                {
                    executedToolCalls.Add(toolCall);

                    _logger?.LogInformation("Executing tool '{ToolName}' (CallId: {CallId})", toolCall.ToolName, toolCall.CallId);

                    AgentToolResult toolResult;
                    try
                    {
                        toolResult = await _toolExecutor.ExecuteAgentToolCallAsync(toolCall, currentTaskId, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        // Guard: ToolExecutor should not throw, but protect operator loop regardless
                        toolResult = new AgentToolResult(toolCall.CallId, toolCall.ToolName, false, $"Internal tool execution error: {ex.Message}", ex.Message);
                    }

                    toolResults.Add(toolResult);

                    // Feed tool result back into conversation for subsequent LLM turn
                    messages.Add(AgentMessage.Tool(toolCall.CallId, toolCall.ToolName, toolResult.ContentJson));

                    if (currentTask != null && _taskRepository != null)
                    {
                        string outcome = toolResult.IsSuccess ? "Success" : $"Failed ({toolResult.ErrorMessage})";
                        currentTask.ActionsTaken.Add($"[{DateTimeOffset.UtcNow:HH:mm:ss}] Tool '{toolCall.ToolName}': {outcome}");
                        try
                        {
                            await _taskRepository.UpdateAsync(currentTask, cancellationToken);
                        }
                        catch { }
                    }
                }

                // Continue to next turn to let LLM process tool results
                continue;
            }

            // Case B: Final Text Response (No tool calls)
            string finalText = completionResponse.Content ?? string.Empty;

            bool hasUnresolvedToolFailure = false;
            if (toolResults.Count > 0)
            {
                // Check if any tool failed and was not followed by a successful execution of that tool
                for (int i = 0; i < toolResults.Count; i++)
                {
                    if (!toolResults[i].IsSuccess)
                    {
                        string failedToolName = toolResults[i].ToolName;
                        bool subsequentlySucceeded = false;
                        for (int j = i + 1; j < toolResults.Count; j++)
                        {
                            if (string.Equals(toolResults[j].ToolName, failedToolName, StringComparison.OrdinalIgnoreCase) && toolResults[j].IsSuccess)
                            {
                                subsequentlySucceeded = true;
                                break;
                            }
                        }

                        if (!subsequentlySucceeded)
                        {
                            hasUnresolvedToolFailure = true;
                            break;
                        }
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(currentTaskId) && _taskRepository != null)
            {
                // Refresh task state to verify it is not in Waiting (e.g. background job still active)
                var latestTask = await _taskRepository.GetByIdAsync(currentTaskId, cancellationToken);
                if (latestTask != null)
                {
                    latestTask.ResultSummary = finalText;

                    if (latestTask.Status == AgentTaskStatus.Running)
                    {
                        if (hasUnresolvedToolFailure)
                        {
                            await _taskRepository.UpdateAsync(latestTask, cancellationToken);
                            await _taskRepository.TransitionStatusAsync(currentTaskId, AgentTaskStatus.Failed, "Task failed due to unresolved tool execution error.", cancellationToken);
                            return new AgentOperatorResponse(
                                ResponseText: finalText,
                                TaskId: currentTaskId,
                                IsSuccess: false,
                                IsCompleted: false,
                                ExecutedToolCalls: executedToolCalls,
                                ErrorMessage: "Task execution contained unresolved tool failures.");
                        }
                        else
                        {
                            await _taskRepository.UpdateAsync(latestTask, cancellationToken);
                            await _taskRepository.TransitionStatusAsync(currentTaskId, AgentTaskStatus.Completed, "Task completed successfully.", cancellationToken);
                            return new AgentOperatorResponse(
                                ResponseText: finalText,
                                TaskId: currentTaskId,
                                IsSuccess: true,
                                IsCompleted: true,
                                ExecutedToolCalls: executedToolCalls);
                        }
                    }
                    else if (latestTask.Status == AgentTaskStatus.Waiting || latestTask.Status == AgentTaskStatus.NeedsApproval)
                    {
                        await _taskRepository.UpdateAsync(latestTask, cancellationToken);
                        return new AgentOperatorResponse(
                            ResponseText: finalText,
                            TaskId: currentTaskId,
                            IsSuccess: true,
                            IsCompleted: false,
                            ExecutedToolCalls: executedToolCalls);
                    }
                    else if (latestTask.Status == AgentTaskStatus.Failed || latestTask.Status == AgentTaskStatus.Cancelled)
                    {
                        await _taskRepository.UpdateAsync(latestTask, cancellationToken);
                        return new AgentOperatorResponse(
                            ResponseText: finalText,
                            TaskId: currentTaskId,
                            IsSuccess: false,
                            IsCompleted: false,
                            ExecutedToolCalls: executedToolCalls,
                            ErrorMessage: latestTask.Status == AgentTaskStatus.Cancelled ? "Operation cancelled." : "Task execution failed.");
                    }
                }
            }

            return new AgentOperatorResponse(
                ResponseText: finalText,
                TaskId: currentTaskId,
                IsSuccess: !hasUnresolvedToolFailure,
                IsCompleted: true,
                ExecutedToolCalls: executedToolCalls);
        }

        // Case C: Turn limit exhausted without reaching completion
        string timeoutMsg = $"Maximum execution turns ({MaxTurns}) reached without final answer.";
        _logger?.LogWarning("AgentOperator exhausted max turns ({MaxTurns}) for request '{Prompt}'", MaxTurns, request.UserPrompt);

        if (!string.IsNullOrWhiteSpace(currentTaskId) && _taskRepository != null)
        {
            try
            {
                await _taskRepository.TransitionStatusAsync(currentTaskId, AgentTaskStatus.Failed, timeoutMsg, CancellationToken.None);
            }
            catch { }
        }

        return new AgentOperatorResponse(
            ResponseText: timeoutMsg,
            TaskId: currentTaskId,
            IsSuccess: false,
            IsCompleted: false,
            ExecutedToolCalls: executedToolCalls,
            ErrorMessage: timeoutMsg);
    }

    private async Task<string> CreateAndStartDurableTaskAsync(string prompt, CancellationToken cancellationToken)
    {
        if (_taskRepository == null) return Guid.NewGuid().ToString("N");

        var task = new AgentTask
        {
            Title = prompt.Length > 50 ? prompt[..47] + "..." : prompt,
            NaturalLanguageRequest = prompt,
            StructuredGoal = prompt,
            Status = AgentTaskStatus.Draft
        };

        var created = await _taskRepository.CreateAsync(task, cancellationToken);
        await _taskRepository.TransitionStatusAsync(created.Id, AgentTaskStatus.Pending, "Task queued for execution.", cancellationToken);
        await _taskRepository.TransitionStatusAsync(created.Id, AgentTaskStatus.Running, "AgentOperator started execution.", cancellationToken);

        return created.Id;
    }
}
