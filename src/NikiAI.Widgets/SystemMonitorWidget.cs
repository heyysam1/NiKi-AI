using System.Diagnostics;
using NikiAI.Core.Widgets;

namespace NikiAI.Widgets;

/// <summary>
/// System Monitor Widget: Displays CPU/Memory utilization metrics.
/// Consumes standard .NET diagnostics without introducing external telemetry services.
/// Refreshed on a 60-second cadence by the centralized coordinator.
/// </summary>
public class SystemMonitorWidget : BaseWidget
{
    private DateTime _lastCpuSampleTime = DateTime.UtcNow;
    private TimeSpan _lastTotalProcessorTime = TimeSpan.Zero;

    public override string Id => "system_monitor";
    public override string Title => "System Monitor";
    public override WidgetCategory Category => WidgetCategory.System;
    public override string IconGlyph => "📊";

    public SystemMonitorWidget()
    {
        ActionLabel = "Refresh";
        UpdateMetrics();
    }

    public override Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        UpdateMetrics();
        return Task.CompletedTask;
    }

    public override Task ExecuteActionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        UpdateMetrics();
        return Task.CompletedTask;
    }

    private void UpdateMetrics()
    {
        try
        {
            using var currentProcess = Process.GetCurrentProcess();
            var workingSetMb = currentProcess.WorkingSet64 / (1024.0 * 1024.0);
            var threadCount = currentProcess.Threads.Count;

            var now = DateTime.UtcNow;
            var cpuSpan = now - _lastCpuSampleTime;
            double cpuPercent = 0.0;

            if (_lastTotalProcessorTime != TimeSpan.Zero && cpuSpan.TotalMilliseconds > 100)
            {
                var processorDelta = currentProcess.TotalProcessorTime - _lastTotalProcessorTime;
                cpuPercent = Math.Clamp(
                    (processorDelta.TotalMilliseconds / (cpuSpan.TotalMilliseconds * Environment.ProcessorCount)) * 100.0,
                    0.0,
                    100.0
                );
            }

            _lastCpuSampleTime = now;
            _lastTotalProcessorTime = currentProcess.TotalProcessorTime;

            PrimaryDisplayValue = $"CPU: {cpuPercent:F1}%";
            SecondaryDisplayValue = $"RAM: {workingSetMb:F0} MB ({threadCount} threads)";
            PresentationState = WidgetPresentationState.Active;
            HasError = false;
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = ex.Message;
            PrimaryDisplayValue = "Metrics unavailable";
            PresentationState = WidgetPresentationState.Error;
        }
    }
}
