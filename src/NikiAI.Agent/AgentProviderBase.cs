using NikiAI.Core.Agent;

namespace NikiAI.Agent;

/// <summary>
/// Abstract base class for AI Provider implementations.
/// Handles shared configuration and default validation.
/// </summary>
public abstract class AgentProviderBase : IAgentProvider
{
    public abstract string ProviderId { get; }
    public abstract string ProviderName { get; }

    public ProviderConfig Config { get; set; } = new();

    public abstract Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken = default);

    public abstract Task<ChatCompletionResponse> GenerateResponseAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken = default);

    public abstract IAsyncEnumerable<string> StreamResponseAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken = default);
}
