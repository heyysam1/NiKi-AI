using System.Diagnostics;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NikiAI.Core.Agent;
using NikiAI.Core.Logging;
using NikiAI.Core.Security;

namespace NikiAI.Agent;

/// <summary>
/// Production OpenAI-compatible REST AI provider.
/// Connects to OpenAI, OpenRouter, Azure OpenAI, or local offline LLM runtimes (Ollama, LM Studio, vLLM).
/// Uses standard HttpClient without bloated third-party SDK dependencies.
/// </summary>
public class OpenAiCompatibleProvider : AgentProviderBase
{
    private readonly HttpClient _httpClient;
    private readonly ISecureSettingsStore _secureSettingsStore;
    private readonly ILogger<OpenAiCompatibleProvider>? _logger;

    public override string ProviderId => "openai-compatible";
    public override string ProviderName => "OpenAI Compatible / Local LLM";

    public OpenAiCompatibleProvider(
        HttpClient httpClient,
        ISecureSettingsStore secureSettingsStore,
        ILogger<OpenAiCompatibleProvider>? logger = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _secureSettingsStore = secureSettingsStore ?? throw new ArgumentNullException(nameof(secureSettingsStore));
        _logger = logger;
    }

    public async Task LoadConfigFromStoreAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var endpoint = await _secureSettingsStore.GetSecretAsync("ai_provider_endpoint", cancellationToken);
            if (!string.IsNullOrWhiteSpace(endpoint))
            {
                Config.EndpointUrl = endpoint.Trim();
            }

            var model = await _secureSettingsStore.GetSecretAsync("ai_provider_model", cancellationToken);
            if (!string.IsNullOrWhiteSpace(model))
            {
                Config.ModelName = model.Trim();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load provider configuration from secure store.");
        }
    }

    public async Task SaveConfigToStoreAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(Config.EndpointUrl))
            {
                await _secureSettingsStore.SetSecretAsync("ai_provider_endpoint", Config.EndpointUrl.Trim(), cancellationToken);
            }
            if (!string.IsNullOrWhiteSpace(Config.ModelName))
            {
                await _secureSettingsStore.SetSecretAsync("ai_provider_model", Config.ModelName.Trim(), cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to save provider configuration to secure store.");
        }
    }

    public override async Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await LoadConfigFromStoreAsync(cancellationToken);
            var apiKey = await _secureSettingsStore.GetSecretAsync(Config.ApiKeySecretKey, cancellationToken);
            var endpoint = GetEndpointUrl();

            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            }

            // Minimal ping payload
            var pingPayload = new
            {
                model = Config.ModelName,
                messages = new[]
                {
                    new { role = "user", content = "ping" }
                },
                max_tokens = 1
            };

            req.Content = new StringContent(JsonSerializer.Serialize(pingPayload), Encoding.UTF8, "application/json");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(15)); // 15s timeout for connection test

            var res = await _httpClient.SendAsync(req, cts.Token);
            sw.Stop();

            if (!res.IsSuccessStatusCode)
            {
                var errorBody = await res.Content.ReadAsStringAsync(cancellationToken);
                var safeError = SecretRedactor.Redact(errorBody);
                var reason = ClassifyStatusCode(res.StatusCode, safeError);
                _logger?.LogWarning("Connection test failed with HTTP {Code}: {Reason}", (int)res.StatusCode, reason);
                return ConnectionTestResult.Failure(sw.ElapsedMilliseconds, reason);
            }

            var responseJson = await res.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(responseJson);
            var modelUsed = doc.RootElement.TryGetProperty("model", out var modelProp)
                ? modelProp.GetString() ?? Config.ModelName
                : Config.ModelName;

            _logger?.LogInformation("AI Provider connection test succeeded in {Latency}ms using model {Model}", sw.ElapsedMilliseconds, modelUsed);
            return ConnectionTestResult.Success(sw.ElapsedMilliseconds, modelUsed);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            var msg = "Connection timed out after 15 seconds. Please check the Base URL and network reachability.";
            _logger?.LogWarning(ex, "{Message}", msg);
            return ConnectionTestResult.Failure(sw.ElapsedMilliseconds, msg);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            return ConnectionTestResult.Failure(sw.ElapsedMilliseconds, "Connection test cancelled by user.");
        }
        catch (Exception ex)
        {
            sw.Stop();
            var safeMessage = SecretRedactor.Redact(ex.Message);
            _logger?.LogError(ex, "Connection test encountered error: {Message}", safeMessage);
            return ConnectionTestResult.Failure(sw.ElapsedMilliseconds, $"Connection error: {safeMessage}");
        }
    }

    public override async Task<ChatCompletionResponse> GenerateResponseAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var apiKey = await _secureSettingsStore.GetSecretAsync(Config.ApiKeySecretKey, cancellationToken);
        var endpoint = GetEndpointUrl();

        using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        var messagesList = BuildMessagesPayload(request);
        var payloadDict = new Dictionary<string, object?>
        {
            ["model"] = request.Model ?? Config.ModelName,
            ["messages"] = messagesList,
            ["temperature"] = request.Temperature ?? Config.Temperature,
            ["max_tokens"] = request.MaxTokens ?? Config.MaxTokens
        };

        if (request.Tools != null && request.Tools.Count > 0)
        {
            var toolsList = request.Tools.Select(t =>
            {
                object schemaObj;
                try
                {
                    schemaObj = JsonSerializer.Deserialize<JsonElement>(t.ParametersJsonSchema);
                }
                catch
                {
                    schemaObj = new { type = "object", properties = new { } };
                }

                return new
                {
                    type = "function",
                    function = new
                    {
                        name = t.Name,
                        description = t.Description,
                        parameters = schemaObj
                    }
                };
            }).ToList();

            payloadDict["tools"] = toolsList;
        }

        req.Content = new StringContent(JsonSerializer.Serialize(payloadDict), Encoding.UTF8, "application/json");

        using var res = await _httpClient.SendAsync(req, cancellationToken);
        if (!res.IsSuccessStatusCode)
        {
            var errBody = await res.Content.ReadAsStringAsync(cancellationToken);
            var safeErr = SecretRedactor.Redact(errBody);
            var reason = ClassifyStatusCode(res.StatusCode, safeErr);
            throw new HttpRequestException($"AI Provider error (HTTP {(int)res.StatusCode}): {reason}");
        }

        var responseJson = await res.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            {
                throw new InvalidOperationException("Malformed AI provider response: missing or empty choices array.");
            }

            var firstChoice = choices[0];
            var message = firstChoice.GetProperty("message");
            var content = message.TryGetProperty("content", out var cProp) ? (cProp.GetString() ?? string.Empty) : string.Empty;

            List<AgentToolCall>? toolCalls = null;
            if (message.TryGetProperty("tool_calls", out var tcProp) && tcProp.ValueKind == JsonValueKind.Array)
            {
                toolCalls = new List<AgentToolCall>();
                foreach (var tc in tcProp.EnumerateArray())
                {
                    var id = tc.TryGetProperty("id", out var idProp) ? (idProp.GetString() ?? Guid.NewGuid().ToString()) : Guid.NewGuid().ToString();
                    if (tc.TryGetProperty("function", out var fnProp))
                    {
                        var name = fnProp.TryGetProperty("name", out var nProp) ? (nProp.GetString() ?? string.Empty) : string.Empty;
                        var args = fnProp.TryGetProperty("arguments", out var aProp) ? (aProp.GetString() ?? "{}") : "{}";
                        toolCalls.Add(new AgentToolCall(id, name, args));
                    }
                }
            }

            string? finishReason = firstChoice.TryGetProperty("finish_reason", out var finishProp)
                ? finishProp.GetString()
                : null;

            string? model = root.TryGetProperty("model", out var modelProp)
                ? modelProp.GetString()
                : null;

            TokenUsage? usage = null;
            if (root.TryGetProperty("usage", out var usageProp))
            {
                int pTokens = usageProp.TryGetProperty("prompt_tokens", out var pt) ? pt.GetInt32() : 0;
                int cTokens = usageProp.TryGetProperty("completion_tokens", out var ct) ? ct.GetInt32() : 0;
                int tTokens = usageProp.TryGetProperty("total_tokens", out var tt) ? tt.GetInt32() : (pTokens + cTokens);
                usage = new TokenUsage(pTokens, cTokens, tTokens);
            }

            return new ChatCompletionResponse(content, model, usage, finishReason, toolCalls);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Failed to parse AI provider JSON response.", ex);
        }
    }

    public override async IAsyncEnumerable<string> StreamResponseAsync(
        ChatCompletionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var apiKey = await _secureSettingsStore.GetSecretAsync(Config.ApiKeySecretKey, cancellationToken);
        var endpoint = GetEndpointUrl();

        using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        var messagesList = BuildMessagesPayload(request);
        var payload = new
        {
            model = request.Model ?? Config.ModelName,
            messages = messagesList,
            temperature = request.Temperature ?? Config.Temperature,
            max_tokens = request.MaxTokens ?? Config.MaxTokens,
            stream = true
        };

        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var res = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!res.IsSuccessStatusCode)
        {
            var errBody = await res.Content.ReadAsStringAsync(cancellationToken);
            var safeErr = SecretRedactor.Redact(errBody);
            throw new HttpRequestException($"Streaming request failed (HTTP {(int)res.StatusCode}): {ClassifyStatusCode(res.StatusCode, safeErr)}");
        }

        using var stream = await res.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line)) continue;

            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var data = line["data:".Length..].Trim();
            if (data == "[DONE]")
            {
                break;
            }

            string? token = null;
            try
            {
                using var doc = JsonDocument.Parse(data);
                if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                {
                    var first = choices[0];
                    if (first.TryGetProperty("delta", out var delta) && delta.TryGetProperty("content", out var contentProp))
                    {
                        token = contentProp.GetString();
                    }
                }
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"Malformed SSE stream chunk: '{data}'.", ex);
            }

            if (!string.IsNullOrEmpty(token))
            {
                yield return token;
            }
        }
    }

    private string GetEndpointUrl()
    {
        var baseUri = Config.EndpointUrl.TrimEnd('/');
        if (baseUri.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            return baseUri;
        }
        return $"{baseUri}/chat/completions";
    }

    private static List<object> BuildMessagesPayload(ChatCompletionRequest request)
    {
        var list = new List<object>();

        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            list.Add(new { role = "system", content = request.SystemPrompt });
        }

        foreach (var msg in request.Messages)
        {
            if (msg.Role == "assistant" && msg.ToolCalls != null && msg.ToolCalls.Count > 0)
            {
                var toolCallsPayload = msg.ToolCalls.Select(tc => new
                {
                    id = tc.CallId,
                    type = "function",
                    function = new
                    {
                        name = tc.ToolName,
                        arguments = tc.ArgumentsJson
                    }
                }).ToArray();

                list.Add(new
                {
                    role = "assistant",
                    content = msg.Content ?? string.Empty,
                    tool_calls = toolCallsPayload
                });
            }
            else if (msg.Role == "tool")
            {
                list.Add(new
                {
                    role = "tool",
                    tool_call_id = msg.ToolCallId ?? string.Empty,
                    name = msg.ToolName ?? string.Empty,
                    content = msg.Content
                });
            }
            else
            {
                list.Add(new { role = msg.Role, content = msg.Content });
            }
        }

        return list;
    }

    private static string ClassifyStatusCode(System.Net.HttpStatusCode code, string errorDetails)
    {
        return code switch
        {
            System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden =>
                "Authentication failed (HTTP 401/403). Please verify your API Key.",
            System.Net.HttpStatusCode.NotFound =>
                "Endpoint or model not found (HTTP 404). Please verify Base URL and Model Name.",
            System.Net.HttpStatusCode.TooManyRequests =>
                "Rate limit exceeded or quota exhausted (HTTP 429).",
            System.Net.HttpStatusCode.InternalServerError or System.Net.HttpStatusCode.BadGateway or System.Net.HttpStatusCode.ServiceUnavailable =>
                "AI provider internal server error (HTTP 500/502/503). Please try again later.",
            _ => $"HTTP {(int)code}: {errorDetails}"
        };
    }
}
