using System.Text.Json;
using NikiAI.Core.Browser;

namespace NikiAI.Browser;

/// <summary>
/// CDP-based browser driver for Microsoft Edge and Brave.
/// Implements low-level browser automation via Chrome DevTools Protocol over WebSockets.
/// </summary>
public class CdpBrowserDriver : IBrowserDriver
{
    private readonly BrowserProcessManager _processManager;
    private CdpClient? _cdpClient;
    private bool _isDisposed;

    public SupportedBrowser BrowserType => _processManager.BrowserType;
    public bool IsRunning => _processManager.IsRunning && _cdpClient != null && _cdpClient.IsConnected;

    public CdpBrowserDriver(
        IBrowserService browserService,
        SupportedBrowser preferredBrowser = SupportedBrowser.Edge)
    {
        _processManager = new BrowserProcessManager(browserService, preferredBrowser);
    }

    public async Task LaunchAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning) return;

        var pageWsUri = await _processManager.LaunchAsync(cancellationToken);
        _cdpClient = new CdpClient();
        await _cdpClient.ConnectAsync(pageWsUri, cancellationToken);

        // Enable Page and Runtime domains
        await _cdpClient.SendCommandAsync("Page.enable", null, TimeSpan.FromSeconds(5), cancellationToken);
        await _cdpClient.SendCommandAsync("Runtime.enable", null, TimeSpan.FromSeconds(5), cancellationToken);
    }

    public async Task<string> NavigateAsync(string url, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        // Security check: block internal browser schemes chrome://, chrome-extension://, javascript:, data:
        if (url.StartsWith("chrome://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase))
        {
            throw new BlockedNavigationException($"Navigation to internal browser URL ('{url}') is prohibited.");
        }

        if (url.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            throw new BlockedNavigationException($"Navigation to scheme in '{url}' is blocked for security reasons.");
        }

        if (!IsRunning)
        {
            await LaunchAsync(cancellationToken);
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCts.CancelAfter(timeout);

        var loadTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnEvent(string method, JsonElement @params)
        {
            if (method is "Page.loadEventFired" or "Page.frameStoppedLoading" or "Page.domContentEventFired")
            {
                loadTcs.TrySetResult();
            }
        }

        _cdpClient!.EventReceived += OnEvent;
        try
        {
            var navResult = await _cdpClient.SendCommandAsync(
                "Page.navigate",
                new { url },
                timeout,
                linkedCts.Token);

            if (navResult.TryGetProperty("errorText", out var errorElem))
            {
                var err = errorElem.GetString();
                if (!string.IsNullOrEmpty(err) && err != "net::ERR_ABORTED")
                {
                    throw new BrowserNavigationException($"Failed to navigate to '{url}': {err}");
                }
            }

            // Await load event or brief grace window before polling DOM readyState
            await Task.WhenAny(loadTcs.Task, Task.Delay(500, linkedCts.Token));

            // Verify DOM is ready and page location has transitioned
            await WaitForPageReadyAsync(url, timeout, linkedCts.Token);
            return url;
        }
        finally
        {
            _cdpClient.EventReceived -= OnEvent;
        }
    }

    private async Task WaitForPageReadyAsync(string targetUrl, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var readyState = await EvaluateScriptAsync<string>(
                    "document.readyState",
                    TimeSpan.FromSeconds(2),
                    cancellationToken);

                var href = await EvaluateScriptAsync<string>(
                    "document.location.href",
                    TimeSpan.FromSeconds(2),
                    cancellationToken);

                bool hrefMatches = string.Equals(targetUrl, "about:blank", StringComparison.OrdinalIgnoreCase) ||
                                   (!string.IsNullOrEmpty(href) && href != "about:blank");

                if (hrefMatches &&
                    (string.Equals(readyState, "complete", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(readyState, "interactive", StringComparison.OrdinalIgnoreCase)))
                {
                    // Brief settling delay for DOM mutations
                    await Task.Delay(50, cancellationToken);
                    return;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Page may be transitioning; retry
            }

            await Task.Delay(50, cancellationToken);
        }

        throw new BrowserTimeoutException($"Timed out waiting for page '{targetUrl}' to become ready after {timeout.TotalSeconds:F1}s.");
    }

    public async Task<T?> EvaluateScriptAsync<T>(string expression, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);

        if (!IsRunning)
        {
            throw new BrowserCrashException("Browser is not running.");
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCts.CancelAfter(timeout);

        var evalResult = await _cdpClient!.SendCommandAsync(
            "Runtime.evaluate",
            new
            {
                expression,
                returnByValue = true,
                awaitPromise = true
            },
            timeout,
            linkedCts.Token);

        if (evalResult.TryGetProperty("exceptionDetails", out var exElem))
        {
            var text = exElem.TryGetProperty("text", out var t) ? t.GetString() : "Script execution failed";
            throw new BrowserNavigationException($"JavaScript evaluation failed: {text}");
        }

        if (evalResult.TryGetProperty("result", out var resultObj))
        {
            if (resultObj.TryGetProperty("value", out var valElem))
            {
                if (typeof(T) == typeof(string))
                {
                    return (T)(object)valElem.ToString();
                }

                var rawJson = valElem.GetRawText();
                return JsonSerializer.Deserialize<T>(rawJson);
            }
        }

        return default;
    }

    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        if (_cdpClient != null)
        {
            await _cdpClient.DisposeAsync();
            _cdpClient = null;
        }

        _processManager.TerminateProcess();
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        if (_cdpClient != null)
        {
            await _cdpClient.DisposeAsync();
            _cdpClient = null;
        }

        await _processManager.DisposeAsync();
    }
}
