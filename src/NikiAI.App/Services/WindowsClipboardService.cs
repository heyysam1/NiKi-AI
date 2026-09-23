using System.Runtime.InteropServices;
using NikiAI.Core.Tools;
using WpfClipboard = System.Windows.Clipboard;

namespace NikiAI.App.Services;

/// <summary>
/// Safe Windows platform clipboard service.
/// Uses STA thread execution and handles clipboard access locks gracefully.
/// Strictly local: content is returned solely to the tool execution pipeline
/// and is never transmitted to remote AI endpoints.
/// </summary>
public class WindowsClipboardService : IClipboardService
{
    public Task<string?> GetTextAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return RunOnStaThread(() =>
        {
            try
            {
                if (WpfClipboard.ContainsText())
                {
                    return WpfClipboard.GetText();
                }
                return null;
            }
            catch (ExternalException)
            {
                // Clipboard may be locked by another application; attempt one brief retry
                Thread.Sleep(50);
                if (WpfClipboard.ContainsText())
                {
                    return WpfClipboard.GetText();
                }
                return null;
            }
        });
    }

    public Task SetTextAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        cancellationToken.ThrowIfCancellationRequested();

        return RunOnStaThread(() =>
        {
            try
            {
                WpfClipboard.SetText(text);
            }
            catch (ExternalException)
            {
                Thread.Sleep(50);
                WpfClipboard.SetText(text);
            }
        });
    }

    private static async Task RunOnStaThread(Action action)
    {
        var app = System.Windows.Application.Current;
        if (app != null && !app.Dispatcher.HasShutdownStarted)
        {
            await app.Dispatcher.InvokeAsync(() =>
            {
                for (int i = 0; i < 5; i++)
                {
                    try
                    {
                        action();
                        return;
                    }
                    catch (ExternalException) when (i < 4)
                    {
                        Thread.Sleep(50);
                    }
                }
            });
            return;
        }

        var tcs = new TaskCompletionSource();

        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            try
            {
                action();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
            return;
        }

        var thread = new Thread(() =>
        {
            try
            {
                action();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        })
        {
            IsBackground = true
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        await tcs.Task;
    }

    private static async Task<T> RunOnStaThread<T>(Func<T> func)
    {
        var app = System.Windows.Application.Current;
        if (app != null && !app.Dispatcher.HasShutdownStarted)
        {
            return await app.Dispatcher.InvokeAsync(() =>
            {
                for (int i = 0; i < 5; i++)
                {
                    try
                    {
                        return func();
                    }
                    catch (ExternalException) when (i < 4)
                    {
                        Thread.Sleep(50);
                    }
                }
                return func();
            });
        }

        var tcs = new TaskCompletionSource<T>();

        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            try
            {
                tcs.SetResult(func());
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
            return await tcs.Task;
        }

        var thread = new Thread(() =>
        {
            try
            {
                tcs.SetResult(func());
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        })
        {
            IsBackground = true
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return await tcs.Task;
    }
}
