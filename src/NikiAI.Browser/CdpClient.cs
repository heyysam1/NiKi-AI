using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using NikiAI.Core.Browser;

namespace NikiAI.Browser;

/// <summary>
/// Lightweight Chrome DevTools Protocol (CDP) WebSocket client.
/// Uses native System.Net.WebSockets.ClientWebSocket with zero external dependencies.
/// Strictly connects to local Microsoft Edge or Brave debugging endpoints.
/// </summary>
public class CdpClient : IAsyncDisposable
{
    private readonly ClientWebSocket _webSocket = new();
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pendingRequests = new();
    private readonly CancellationTokenSource _receiveCts = new();
    private Task? _receiveLoopTask;
    private int _nextId = 1;
    private bool _isDisposed;

    public event Action<string, JsonElement>? EventReceived;

    public bool IsConnected => _webSocket.State == WebSocketState.Open;

    public async Task ConnectAsync(Uri pageWebSocketUri, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pageWebSocketUri);

        try
        {
            await _webSocket.ConnectAsync(pageWebSocketUri, cancellationToken);
            _receiveLoopTask = Task.Run(ReceiveLoopAsync);
        }
        catch (Exception ex)
        {
            throw new BrowserCrashException($"Failed to connect to browser CDP endpoint at '{pageWebSocketUri}': {ex.Message}", ex);
        }
    }

    public async Task<JsonElement> SendCommandAsync(
        string method,
        object? parameters = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        if (_isDisposed || _webSocket.State != WebSocketState.Open)
        {
            throw new BrowserCrashException("CDP connection is closed or disconnected.");
        }

        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingRequests[id] = tcs;

        var commandObj = new Dictionary<string, object?>
        {
            ["id"] = id,
            ["method"] = method
        };

        if (parameters != null)
        {
            commandObj["params"] = parameters;
        }

        var json = JsonSerializer.Serialize(commandObj);
        var bytes = Encoding.UTF8.GetBytes(json);

        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(30);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCts.CancelAfter(effectiveTimeout);

        try
        {
            await _webSocket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                true,
                linkedCts.Token);

            return await tcs.Task.WaitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _pendingRequests.TryRemove(id, out _);
            throw;
        }
        catch (OperationCanceledException)
        {
            _pendingRequests.TryRemove(id, out _);
            throw new BrowserTimeoutException($"CDP command '{method}' timed out after {effectiveTimeout.TotalSeconds:F1}s.");
        }
        catch (WebSocketException ex)
        {
            _pendingRequests.TryRemove(id, out _);
            throw new BrowserCrashException($"CDP socket communication error while executing '{method}': {ex.Message}", ex);
        }
    }

    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[32 * 1024];
        var ms = new MemoryStream();

        try
        {
            while (!_receiveCts.Token.IsCancellationRequested && _webSocket.State == WebSocketState.Open)
            {
                ms.SetLength(0);
                WebSocketReceiveResult result;

                do
                {
                    result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _receiveCts.Token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await HandleDisconnectAsync();
                        return;
                    }
                    ms.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                if (ms.Length == 0) continue;

                var messageJson = Encoding.UTF8.GetString(ms.ToArray());
                ProcessIncomingMessage(messageJson);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            FailAllPending(new BrowserCrashException($"Browser connection terminated unexpectedly: {ex.Message}", ex));
        }
        finally
        {
            await HandleDisconnectAsync();
        }
    }

    private void ProcessIncomingMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("id", out var idElem) && idElem.TryGetInt32(out var id))
            {
                if (_pendingRequests.TryRemove(id, out var tcs))
                {
                    if (root.TryGetProperty("error", out var errorElem))
                    {
                        var errorMsg = errorElem.TryGetProperty("message", out var msgElem)
                            ? msgElem.GetString()
                            : errorElem.ToString();

                        tcs.TrySetException(new BrowserNavigationException($"CDP error response: {errorMsg}"));
                    }
                    else if (root.TryGetProperty("result", out var resultElem))
                    {
                        tcs.TrySetResult(resultElem.Clone());
                    }
                    else
                    {
                        tcs.TrySetResult(default);
                    }
                }
            }
            else if (root.TryGetProperty("method", out var methodElem))
            {
                var method = methodElem.GetString();
                if (!string.IsNullOrEmpty(method))
                {
                    var @params = root.TryGetProperty("params", out var pElem) ? pElem.Clone() : default;
                    EventReceived?.Invoke(method, @params);
                }
            }
        }
        catch { }
    }

    private async Task HandleDisconnectAsync()
    {
        FailAllPending(new BrowserCrashException("Browser connection disconnected."));
        if (_webSocket.State == WebSocketState.Open || _webSocket.State == WebSocketState.CloseReceived)
        {
            try
            {
                await _webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
            }
            catch { }
        }
    }

    private void FailAllPending(Exception ex)
    {
        foreach (var kvp in _pendingRequests)
        {
            if (_pendingRequests.TryRemove(kvp.Key, out var tcs))
            {
                tcs.TrySetException(ex);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _receiveCts.Cancel();
        FailAllPending(new OperationCanceledException("CdpClient is disposed."));

        try
        {
            if (_webSocket.State == WebSocketState.Open)
            {
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disposing", CancellationToken.None);
            }
        }
        catch { }

        _webSocket.Dispose();
        _receiveCts.Dispose();
    }
}
