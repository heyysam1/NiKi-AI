using System.Net;
using System.Text;
using NikiAI.Agent;
using NikiAI.Core.Agent;
using Xunit;

namespace NikiAI.Agent.Tests;

public class OpenAiCompatibleProviderTests
{
    private class MockHttpHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> HandlerFunc { get; set; }

        public MockHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handlerFunc)
        {
            HandlerFunc = handlerFunc;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return HandlerFunc(request, cancellationToken);
        }
    }

    private class FakeSecureStore : NikiAI.Core.Security.ISecureSettingsStore
    {
        public Task SetSecretAsync(string key, string secret, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default)
        {
            if (key.Contains("key", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult<string?>("sk-test-key");
            }
            return Task.FromResult<string?>(null);
        }
        public Task DeleteSecretAsync(string key, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> HasSecretAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private static (OpenAiCompatibleProvider provider, MockHttpHandler handler) CreateProvider(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handlerFunc)
    {
        var handler = new MockHttpHandler(handlerFunc);
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.openai.com/v1")
        };
        var secureStore = new FakeSecureStore();
        var provider = new OpenAiCompatibleProvider(client, secureStore);
        return (provider, handler);
    }

    [Fact]
    public async Task GenerateResponseAsync_When200Ok_ReturnsCorrectResponseAndUsage()
    {
        var responseJson = """
        {
            "id": "chatcmpl-123",
            "model": "gpt-4o-mini",
            "choices": [
                {
                    "message": { "role": "assistant", "content": "Hello there! How can I help?" },
                    "finish_reason": "stop"
                }
            ],
            "usage": {
                "prompt_tokens": 12,
                "completion_tokens": 8,
                "total_tokens": 20
            }
        }
        """;

        var (provider, _) = CreateProvider((req, ct) =>
        {
            var res = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(res);
        });

        var request = new ChatCompletionRequest(new[] { AgentMessage.User("Hi") });
        var response = await provider.GenerateResponseAsync(request);

        Assert.Equal("Hello there! How can I help?", response.Content);
        Assert.Equal("gpt-4o-mini", response.Model);
        Assert.Equal("stop", response.FinishReason);
        Assert.NotNull(response.Usage);
        Assert.Equal(12, response.Usage!.PromptTokens);
        Assert.Equal(8, response.Usage!.CompletionTokens);
        Assert.Equal(20, response.Usage!.TotalTokens);
    }

    [Fact]
    public async Task StreamResponseAsync_When200SseStream_YieldsChunksAccurately()
    {
        var sseData = """
        data: {"choices":[{"delta":{"content":"Hello"}}]}

        data: {"choices":[{"delta":{"content":" world"}}]}

        data: {"choices":[{"delta":{"content":"!"}}]}

        data: [DONE]

        """;

        var (provider, _) = CreateProvider((req, ct) =>
        {
            var res = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(sseData, Encoding.UTF8, "text/event-stream")
            };
            return Task.FromResult(res);
        });

        var request = new ChatCompletionRequest(new[] { AgentMessage.User("Hi") });
        var chunks = new List<string>();

        await foreach (var chunk in provider.StreamResponseAsync(request))
        {
            chunks.Add(chunk);
        }

        Assert.Equal(3, chunks.Count);
        Assert.Equal("Hello", chunks[0]);
        Assert.Equal(" world", chunks[1]);
        Assert.Equal("!", chunks[2]);
        Assert.Equal("Hello world!", string.Join("", chunks));
    }

    [Fact]
    public async Task GenerateResponseAsync_When401Unauthorized_ThrowsHttpRequestExceptionAndNeverProducesFakeSuccess()
    {
        var (provider, _) = CreateProvider((req, ct) =>
        {
            var res = new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("{\"error\":\"Invalid API key\"}")
            };
            return Task.FromResult(res);
        });

        var request = new ChatCompletionRequest(new[] { AgentMessage.User("Hi") });
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => provider.GenerateResponseAsync(request));

        Assert.Contains("401", ex.Message);
    }

    [Fact]
    public async Task GenerateResponseAsync_When429RateLimit_ThrowsHttpRequestExceptionAndNeverProducesFakeSuccess()
    {
        var (provider, _) = CreateProvider((req, ct) =>
        {
            var res = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("{\"error\":\"Rate limit reached\"}")
            };
            return Task.FromResult(res);
        });

        var request = new ChatCompletionRequest(new[] { AgentMessage.User("Hi") });
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => provider.GenerateResponseAsync(request));

        Assert.Contains("429", ex.Message);
    }

    [Fact]
    public async Task GenerateResponseAsync_When500ServerError_ThrowsHttpRequestExceptionAndNeverProducesFakeSuccess()
    {
        var (provider, _) = CreateProvider((req, ct) =>
        {
            var res = new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("{\"error\":\"Internal model failure\"}")
            };
            return Task.FromResult(res);
        });

        var request = new ChatCompletionRequest(new[] { AgentMessage.User("Hi") });
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => provider.GenerateResponseAsync(request));

        Assert.Contains("500", ex.Message);
    }

    [Fact]
    public async Task GenerateResponseAsync_WhenTimeoutOccurs_ThrowsTaskCanceledException()
    {
        var (provider, _) = CreateProvider((req, ct) =>
        {
            throw new TaskCanceledException("HttpClient request timed out after 30000ms.");
        });

        var request = new ChatCompletionRequest(new[] { AgentMessage.User("Hi") });
        await Assert.ThrowsAsync<TaskCanceledException>(() => provider.GenerateResponseAsync(request));
    }

    [Fact]
    public async Task StreamResponseAsync_WhenStreamContainsMalformedJson_ThrowsInvalidOperationException()
    {
        var malformedSse = """
        data: {"choices":[{"delta":{"content":"Valid initial"}}]

        data: {CORRUPTED_JSON_LINE_WITHOUT_QUOTES}

        data: [DONE]
        """;

        var (provider, _) = CreateProvider((req, ct) =>
        {
            var res = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(malformedSse, Encoding.UTF8, "text/event-stream")
            };
            return Task.FromResult(res);
        });

        var request = new ChatCompletionRequest(new[] { AgentMessage.User("Hi") });

        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await foreach (var _ in provider.StreamResponseAsync(request))
            {
            }
        });
    }

    [Fact]
    public async Task GenerateResponseAsync_WhenCancelledByUser_ThrowsOperationCanceledException()
    {
        var (provider, _) = CreateProvider(async (req, ct) =>
        {
            await Task.Delay(5000, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // pre-cancel

        var request = new ChatCompletionRequest(new[] { AgentMessage.User("Hi") });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GenerateResponseAsync(request, cts.Token));
    }

    [Fact]
    public async Task TestConnectionAsync_WhenSuccessful_ReturnsIsSuccessTrue()
    {
        var responseJson = """
        {
            "choices": [ { "message": { "role": "assistant", "content": "pong" } } ]
        }
        """;

        var (provider, _) = CreateProvider((req, ct) =>
        {
            var res = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(res);
        });

        var result = await provider.TestConnectionAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal("gpt-4o-mini", result.ModelUsed);
        Assert.Null(result.ErrorMessage);
    }
}
