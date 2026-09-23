using System.Diagnostics;
using System.Runtime.CompilerServices;
using NikiAI.Core.Agent;

namespace NikiAI.Agent;

public enum MockFailureMode
{
    None,
    Timeout,
    Unauthorized401,
    RateLimit429,
    ServerError500,
    MalformedStream,
    UserCancellation
}

/// <summary>
/// Deterministic mock AI provider for offline testing, test suites, and mandatory verification.
/// Requires zero external network connectivity or paid API credentials.
/// </summary>
public class MockAgentProvider : AgentProviderBase
{
    public override string ProviderId => "mock-provider";
    public override string ProviderName => "Niki AI Offline / Mock Provider";

    public MockFailureMode FailureMode { get; set; } = MockFailureMode.None;
    public long SimulatedLatencyMs { get; set; } = 40;
    public string DefaultResponse { get; set; } = "Hello! I am Niki AI, your intelligent desktop companion. I am running locally and ready to help.";

    public override async Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();

        if (SimulatedLatencyMs > 0)
        {
            await Task.Delay((int)SimulatedLatencyMs, cancellationToken);
        }

        sw.Stop();

        return FailureMode switch
        {
            MockFailureMode.Unauthorized401 =>
                ConnectionTestResult.Failure(sw.ElapsedMilliseconds, "Authentication failed (HTTP 401). Invalid mock credential."),
            MockFailureMode.RateLimit429 =>
                ConnectionTestResult.Failure(sw.ElapsedMilliseconds, "Rate limit exceeded (HTTP 429)."),
            MockFailureMode.ServerError500 =>
                ConnectionTestResult.Failure(sw.ElapsedMilliseconds, "AI provider server error (HTTP 500)."),
            MockFailureMode.Timeout =>
                ConnectionTestResult.Failure(sw.ElapsedMilliseconds, "Connection timed out after 15 seconds."),
            MockFailureMode.UserCancellation =>
                ConnectionTestResult.Failure(sw.ElapsedMilliseconds, "Connection test cancelled by user."),
            _ =>
                ConnectionTestResult.Success(sw.ElapsedMilliseconds, Config.ModelName)
        };
    }

    public override async Task<ChatCompletionResponse> GenerateResponseAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (SimulatedLatencyMs > 0)
        {
            await Task.Delay((int)SimulatedLatencyMs, cancellationToken);
        }

        switch (FailureMode)
        {
            case MockFailureMode.Unauthorized401:
                throw new HttpRequestException("AI Provider error (HTTP 401): Authentication failed. Invalid API Key.");
            case MockFailureMode.RateLimit429:
                throw new HttpRequestException("AI Provider error (HTTP 429): Rate limit exceeded.");
            case MockFailureMode.ServerError500:
                throw new HttpRequestException("AI Provider error (HTTP 500): Server error.");
            case MockFailureMode.Timeout:
                throw new TaskCanceledException("Operation timed out while waiting for AI response.");
            case MockFailureMode.UserCancellation:
                throw new OperationCanceledException("Generation cancelled by user.", cancellationToken);
        }

        var lastUserMsg = request.Messages.LastOrDefault(m => m.Role == "user")?.Content ?? string.Empty;
        var responseText = string.IsNullOrWhiteSpace(lastUserMsg)
            ? DefaultResponse
            : $"Niki AI: Received '{lastUserMsg}'. I've processed your request successfully.";

        var usage = new TokenUsage(
            PromptTokens: Math.Max(5, lastUserMsg.Length / 4),
            CompletionTokens: Math.Max(10, responseText.Length / 4),
            TotalTokens: Math.Max(15, (lastUserMsg.Length + responseText.Length) / 4)
        );

        return new ChatCompletionResponse(
            Content: responseText,
            Model: request.Model ?? Config.ModelName,
            Usage: usage,
            FinishReason: "stop"
        );
    }

    public override async IAsyncEnumerable<string> StreamResponseAsync(
        ChatCompletionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        switch (FailureMode)
        {
            case MockFailureMode.Unauthorized401:
                throw new HttpRequestException("Streaming request failed (HTTP 401): Unauthorized.");
            case MockFailureMode.RateLimit429:
                throw new HttpRequestException("Streaming request failed (HTTP 429): Rate limited.");
            case MockFailureMode.ServerError500:
                throw new HttpRequestException("Streaming request failed (HTTP 500): Server error.");
            case MockFailureMode.Timeout:
                throw new TaskCanceledException("Stream timed out.");
            case MockFailureMode.UserCancellation:
                throw new OperationCanceledException("Stream cancelled by user.", cancellationToken);
        }

        var response = await GenerateResponseAsync(request, cancellationToken);
        var words = response.Content.Split(' ');

        for (int i = 0; i < words.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (FailureMode == MockFailureMode.MalformedStream && i == 2)
            {
                throw new InvalidOperationException("Malformed SSE stream chunk: unexpected byte sequence.");
            }

            var chunk = (i == 0 ? string.Empty : " ") + words[i];
            yield return chunk;

            await Task.Delay(15, cancellationToken);
        }
    }
}
