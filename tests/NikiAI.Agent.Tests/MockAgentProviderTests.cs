using NikiAI.Agent;
using NikiAI.Core.Agent;
using Xunit;

namespace NikiAI.Agent.Tests;

public class MockAgentProviderTests
{
    [Fact]
    public async Task GenerateResponseAsync_WhenDefault_ReturnsSuccessfulResponse()
    {
        var provider = new MockAgentProvider { SimulatedLatencyMs = 0 };
        var request = new ChatCompletionRequest(new[] { AgentMessage.User("Hello Niki") });

        var response = await provider.GenerateResponseAsync(request);

        Assert.NotNull(response);
        Assert.Contains("Hello Niki", response.Content);
        Assert.Equal("gpt-4o-mini", response.Model);
        Assert.NotNull(response.Usage);
        Assert.True(response.Usage!.TotalTokens > 0);
    }

    [Fact]
    public async Task StreamResponseAsync_WhenDefault_StreamsAllWords()
    {
        var provider = new MockAgentProvider { SimulatedLatencyMs = 0 };
        var request = new ChatCompletionRequest(new[] { AgentMessage.User("Quick test") });

        var chunks = new List<string>();
        await foreach (var chunk in provider.StreamResponseAsync(request))
        {
            chunks.Add(chunk);
        }

        Assert.NotEmpty(chunks);
        var fullText = string.Join("", chunks);
        Assert.Contains("Quick test", fullText);
    }

    [Fact]
    public async Task TestConnectionAsync_WhenSuccess_ReturnsValidMetrics()
    {
        var provider = new MockAgentProvider { SimulatedLatencyMs = 10 };
        var result = await provider.TestConnectionAsync();

        Assert.True(result.IsSuccess);
        Assert.True(result.LatencyMs >= 0);
        Assert.Equal("gpt-4o-mini", result.ModelUsed);
        Assert.Null(result.ErrorMessage);
    }

    [Theory]
    [InlineData(MockFailureMode.Unauthorized401, "401")]
    [InlineData(MockFailureMode.RateLimit429, "429")]
    [InlineData(MockFailureMode.ServerError500, "500")]
    public async Task GenerateResponseAsync_WhenHttpError_ThrowsHttpRequestException(MockFailureMode mode, string expectedSubstring)
    {
        var provider = new MockAgentProvider { FailureMode = mode, SimulatedLatencyMs = 0 };
        var request = new ChatCompletionRequest(new[] { AgentMessage.User("Hi") });

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => provider.GenerateResponseAsync(request));
        Assert.Contains(expectedSubstring, ex.Message);
    }

    [Fact]
    public async Task GenerateResponseAsync_WhenTimeout_ThrowsTaskCanceledException()
    {
        var provider = new MockAgentProvider { FailureMode = MockFailureMode.Timeout, SimulatedLatencyMs = 0 };
        var request = new ChatCompletionRequest(new[] { AgentMessage.User("Hi") });

        await Assert.ThrowsAsync<TaskCanceledException>(() => provider.GenerateResponseAsync(request));
    }

    [Fact]
    public async Task GenerateResponseAsync_WhenCancelled_ThrowsOperationCanceledException()
    {
        var provider = new MockAgentProvider { FailureMode = MockFailureMode.UserCancellation, SimulatedLatencyMs = 0 };
        var request = new ChatCompletionRequest(new[] { AgentMessage.User("Hi") });

        await Assert.ThrowsAsync<OperationCanceledException>(() => provider.GenerateResponseAsync(request));
    }

    [Fact]
    public async Task StreamResponseAsync_WhenMalformedStream_ThrowsInvalidOperationException()
    {
        var provider = new MockAgentProvider { FailureMode = MockFailureMode.MalformedStream, SimulatedLatencyMs = 0 };
        var request = new ChatCompletionRequest(new[] { AgentMessage.User("This is a long sentence to trigger stream error") });

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in provider.StreamResponseAsync(request))
            {
            }
        });
    }

    [Theory]
    [InlineData(MockFailureMode.Unauthorized401)]
    [InlineData(MockFailureMode.RateLimit429)]
    [InlineData(MockFailureMode.ServerError500)]
    [InlineData(MockFailureMode.Timeout)]
    [InlineData(MockFailureMode.UserCancellation)]
    public async Task TestConnectionAsync_WhenFailureModeConfigured_ReturnsIsSuccessFalse(MockFailureMode mode)
    {
        var provider = new MockAgentProvider { FailureMode = mode, SimulatedLatencyMs = 0 };
        var result = await provider.TestConnectionAsync();

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.ErrorMessage);
    }
}
