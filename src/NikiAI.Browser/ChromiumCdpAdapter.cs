using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using NikiAI.Core.Browser;

namespace NikiAI.Browser;

/// <summary>
/// Active browser connection communicating via Chrome DevTools Protocol.
/// </summary>
public class ChromiumCdpConnection : IBrowserConnection
{
    private readonly Process? _process;
    private readonly CdpClient _cdpClient;
    private readonly string? _tempProfilePath;
    private bool _isDisposed;

    public bool IsConnected => _cdpClient.IsConnected && (_process == null || !_process.HasExited);

    public ChromiumCdpConnection(Process? process, CdpClient cdpClient, string? tempProfilePath)
    {
        _process = process;
        _cdpClient = cdpClient;
        _tempProfilePath = tempProfilePath;
    }

    public async Task<string> NavigateAsync(string url, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        if (url.StartsWith("chrome://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase))
        {
            throw new BlockedNavigationException($"Navigation to internal browser URL ('{url}') is prohibited.");
        }

        var effectiveTimeout = timeout == default ? TimeSpan.FromSeconds(30) : timeout;
        var navParams = new { url };
        var navResponse = await _cdpClient.SendCommandAsync("Page.navigate", navParams, effectiveTimeout, cancellationToken);

        if (navResponse.TryGetProperty("errorText", out var errProp))
        {
            throw new BrowserNavigationException($"Navigation failed: {errProp.GetString()}");
        }

        var script = "(function() { return document.readyState; })()";
        var readyState = await EvaluateScriptAsync<string>(script, TimeSpan.FromSeconds(5), cancellationToken);
        return readyState ?? "complete";
    }

    public async Task<T?> EvaluateScriptAsync<T>(string expression, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        var effectiveTimeout = timeout == default ? TimeSpan.FromSeconds(10) : timeout;

        var evalParams = new
        {
            expression,
            returnByValue = true,
            awaitPromise = true
        };

        var response = await _cdpClient.SendCommandAsync("Runtime.evaluate", evalParams, effectiveTimeout, cancellationToken);
        if (response.TryGetProperty("result", out var resultProp))
        {
            if (resultProp.TryGetProperty("value", out var valueProp))
            {
                return JsonSerializer.Deserialize<T>(valueProp.GetRawText());
            }
        }

        return default;
    }

    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        await DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        await _cdpClient.DisposeAsync();

        if (_process != null && !_process.HasExited)
        {
            try
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync();
            }
            catch
            {
                // Ignored during shutdown
            }
            finally
            {
                _process.Dispose();
            }
        }

        if (!string.IsNullOrEmpty(_tempProfilePath) && Directory.Exists(_tempProfilePath))
        {
            try
            {
                Directory.Delete(_tempProfilePath, recursive: true);
            }
            catch
            {
                // Temporary folder cleanup best-effort
            }
        }

        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Capability-based browser adapter automating Chromium-based browsers via CDP.
/// Works with any Chromium browser (Edge, Chrome, Brave, Chromium, etc.).
/// Selection is based on verified runtime capabilities.
/// </summary>
public class ChromiumCdpAdapter : IBrowserAdapter
{
    public string AdapterKey => "chromium-cdp";

    public Task<bool> CanAutomateAsync(BrowserDescriptor descriptor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        // Verification: family must be Chromium, and executable must exist on disk
        if (descriptor.Family != BrowserFamily.Chromium)
        {
            return Task.FromResult(false);
        }

        if (string.IsNullOrWhiteSpace(descriptor.ExecutablePath) || !File.Exists(descriptor.ExecutablePath))
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }

    public async Task<IBrowserConnection> LaunchOrAttachAsync(
        BrowserDescriptor descriptor, 
        BrowserLaunchOptions options, 
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        var canAutomate = await CanAutomateAsync(descriptor, cancellationToken);
        if (!canAutomate)
        {
            throw new InvalidOperationException($"Browser '{descriptor.FriendlyName}' is not installed or has no compatible adapter.");
        }

        var port = options.RemoteDebuggingPort > 0 ? options.RemoteDebuggingPort : GetAvailablePort();
        var tempProfilePath = Path.Combine(Path.GetTempPath(), $"NikiAI_BrowserAdapter_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempProfilePath);

        var arguments = string.Join(" ",
            options.Headless ? "--headless=new" : string.Empty,
            $"--remote-debugging-port={port}",
            $"--user-data-dir=\"{tempProfilePath}\"",
            "--no-first-run",
            "--no-default-browser-check",
            "--disable-background-networking",
            "--disable-sync",
            "--disable-extensions",
            "--disable-gpu",
            "--mute-audio",
            "about:blank");

        var startInfo = new ProcessStartInfo
        {
            FileName = descriptor.ExecutablePath,
            Arguments = arguments,
            CreateNoWindow = true,
            UseShellExecute = false
        };

        var process = Process.Start(startInfo)
            ?? throw new BrowserCrashException($"Failed to start process for {descriptor.FriendlyName} at '{descriptor.ExecutablePath}'.");

        var pageWsUri = await WaitForPageWebSocketUrlAsync(port, cancellationToken);
        var cdpClient = new CdpClient();
        await cdpClient.ConnectAsync(pageWsUri, cancellationToken);

        await cdpClient.SendCommandAsync("Page.enable", null, TimeSpan.FromSeconds(5), cancellationToken);
        await cdpClient.SendCommandAsync("Runtime.enable", null, TimeSpan.FromSeconds(5), cancellationToken);

        return new ChromiumCdpConnection(process, cdpClient, tempProfilePath);
    }

    private static int GetAvailablePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task<Uri> WaitForPageWebSocketUrlAsync(int port, CancellationToken cancellationToken)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var sw = Stopwatch.StartNew();
        var timeout = TimeSpan.FromSeconds(15);

        while (sw.Elapsed < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var json = await http.GetStringAsync($"http://127.0.0.1:{port}/json/list", cancellationToken);
                using var doc = JsonDocument.Parse(json);
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    if (el.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "page")
                    {
                        if (el.TryGetProperty("webSocketDebuggerUrl", out var wsProp))
                        {
                            var wsUrl = wsProp.GetString();
                            if (!string.IsNullOrEmpty(wsUrl))
                            {
                                return new Uri(wsUrl);
                            }
                        }
                    }
                }
            }
            catch
            {
                // Retry until port is open
            }

            await Task.Delay(100, cancellationToken);
        }

        throw new BrowserTimeoutException($"Timeout waiting for browser CDP endpoint on port {port}.");
    }
}
