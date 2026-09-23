using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using NikiAI.Core.Browser;

namespace NikiAI.Browser;

/// <summary>
/// Manages launching, port binding, health checking, and isolated lifecycle of Microsoft Edge or Brave processes.
/// Google Chrome is strictly prohibited and guarded at all entry points.
/// </summary>
public class BrowserProcessManager : IAsyncDisposable
{
    private readonly IBrowserService _browserService;
    private Process? _process;
    private string? _tempProfilePath;
    private int _debuggingPort;
    private bool _isDisposed;

    public SupportedBrowser BrowserType { get; }
    public bool IsRunning => _process != null && !_process.HasExited;
    public int DebuggingPort => _debuggingPort;

    public BrowserProcessManager(
        IBrowserService browserService,
        SupportedBrowser preferredBrowser = SupportedBrowser.Edge)
    {
        _browserService = browserService ?? throw new ArgumentNullException(nameof(browserService));
        BrowserType = preferredBrowser;
    }

    public async Task<Uri> LaunchAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            return await GetPageWebSocketUrlAsync(cancellationToken);
        }

        if (!_browserService.IsBrowserAvailable(BrowserType))
        {
            throw new BrowserUnavailableException(
                $"Configured browser '{BrowserType}' is not available on this system. Supported browsers are Microsoft Edge and Brave.");
        }

        var executablePath = await _browserService.GetBrowserExecutablePathAsync(BrowserType, cancellationToken);
        BrowserGuardrail.AssertNotChrome(executablePath);

        _debuggingPort = GetAvailablePort();
        _tempProfilePath = Path.Combine(Path.GetTempPath(), $"NikiAI_Browser_{BrowserType}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempProfilePath);

        var arguments = string.Join(" ",
            "--headless=new",
            $"--remote-debugging-port={_debuggingPort}",
            $"--user-data-dir=\"{_tempProfilePath}\"",
            "--no-first-run",
            "--no-default-browser-check",
            "--disable-background-networking",
            "--disable-sync",
            "--disable-extensions",
            "--disable-gpu",
            "--mute-audio",
            "--disable-features=Translate,OptimizationHints",
            "about:blank");

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = arguments,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false
        };

        try
        {
            _process = Process.Start(startInfo)
                ?? throw new BrowserCrashException($"Failed to start process for {BrowserType} at '{executablePath}'.");
        }
        catch (Exception ex) when (ex is not BrowserCrashException)
        {
            CleanupTempProfile();
            throw new BrowserCrashException($"Failed to launch browser executable '{executablePath}': {ex.Message}", ex);
        }

        return await WaitForCdpEndpointAsync(cancellationToken);
    }

    private async Task<Uri> WaitForCdpEndpointAsync(CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow.AddSeconds(10);

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_process == null || _process.HasExited)
            {
                throw new BrowserCrashException($"Browser process for {BrowserType} exited prematurely with code {_process?.ExitCode}.");
            }

            try
            {
                var response = await httpClient.GetStringAsync($"http://127.0.0.1:{_debuggingPort}/json/list", cancellationToken);
                using var doc = JsonDocument.Parse(response);
                if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
                {
                    foreach (var target in doc.RootElement.EnumerateArray())
                    {
                        if (target.TryGetProperty("type", out var typeElem) && typeElem.GetString() == "page" &&
                            target.TryGetProperty("webSocketDebuggerUrl", out var wsElem))
                        {
                            var wsUrl = wsElem.GetString();
                            if (!string.IsNullOrWhiteSpace(wsUrl))
                            {
                                return new Uri(wsUrl);
                            }
                        }
                    }
                }
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { }
            catch (TimeoutException) { }
            catch (JsonException) { }

            await Task.Delay(150, cancellationToken);
        }

        throw new BrowserTimeoutException($"Timed out waiting for CDP debugging endpoint on port {_debuggingPort} for {BrowserType}.");
    }

    public async Task<Uri> GetPageWebSocketUrlAsync(CancellationToken cancellationToken = default)
    {
        if (!IsRunning)
        {
            throw new BrowserCrashException("Browser process is not running.");
        }

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var response = await httpClient.GetStringAsync($"http://127.0.0.1:{_debuggingPort}/json/list", cancellationToken);
        using var doc = JsonDocument.Parse(response);

        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var target in doc.RootElement.EnumerateArray())
            {
                if (target.TryGetProperty("type", out var typeElem) && typeElem.GetString() == "page" &&
                    target.TryGetProperty("webSocketDebuggerUrl", out var wsElem))
                {
                    var wsUrl = wsElem.GetString();
                    if (!string.IsNullOrWhiteSpace(wsUrl))
                    {
                        return new Uri(wsUrl);
                    }
                }
            }

            // Fallback to first available target
            if (doc.RootElement.GetArrayLength() > 0 &&
                doc.RootElement[0].TryGetProperty("webSocketDebuggerUrl", out var firstWsElem))
            {
                var wsUrl = firstWsElem.GetString();
                if (!string.IsNullOrWhiteSpace(wsUrl))
                {
                    return new Uri(wsUrl);
                }
            }
        }

        throw new BrowserCrashException("No active page target found in browser.");
    }

    private static int GetAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public void TerminateProcess()
    {
        if (_process != null && !_process.HasExited)
        {
            try
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(2000);
            }
            catch { }
        }

        _process?.Dispose();
        _process = null;
        CleanupTempProfile();
    }

    private void CleanupTempProfile()
    {
        var path = _tempProfilePath;
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return;
        }

        for (int i = 0; i < 3; i++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                _tempProfilePath = null;
                return;
            }
            catch
            {
                Thread.Sleep(100);
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_isDisposed) return ValueTask.CompletedTask;
        _isDisposed = true;

        TerminateProcess();
        return ValueTask.CompletedTask;
    }
}
