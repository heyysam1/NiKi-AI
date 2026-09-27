namespace NikiAI.Core.Agent;

/// <summary>
/// Execution request for AgentOperator.
/// Supports both casual transient chats and tracked durable operations.
/// </summary>
public sealed record AgentOperatorRequest(
    string UserPrompt,
    string? TaskId = null,
    IReadOnlyList<AgentMessage>? ConversationHistory = null,
    bool ForceDurableTracking = false,
    string? SystemPromptOverride = null);

/// <summary>
/// Execution response from AgentOperator.
/// </summary>
public sealed record AgentOperatorResponse(
    string ResponseText,
    string? TaskId = null,
    bool IsSuccess = true,
    bool IsCompleted = true,
    IReadOnlyList<AgentToolCall>? ExecutedToolCalls = null,
    string? ErrorMessage = null);

/// <summary>
/// High-level conversational orchestration operator.
/// Coordinates native LLM tool calling, multi-turn reasoning, self-correction, and task lifecycle tracking.
/// Never references PermissionEngine; strictly delegates all execution/authorization to IToolExecutor.
/// </summary>
public interface IAgentOperator
{
    Task<AgentOperatorResponse> ExecutePromptAsync(
        string prompt,
        CancellationToken cancellationToken = default);

    Task<AgentOperatorResponse> ExecuteAsync(
        AgentOperatorRequest request,
        CancellationToken cancellationToken = default);
}
