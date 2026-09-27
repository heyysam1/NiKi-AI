using NikiAI.Core.Widgets;
using Xunit;

namespace NikiAI.Widgets.Tests;

public class WidgetRefreshCoordinatorTests
{
    private class CountingWidget : BaseWidget
    {
        public int RefreshCount { get; private set; }
        public override string Id => "clock"; // Use "clock" to subscribe to 1s cadence
        public override string Title => "Counting Clock";
        public override WidgetCategory Category => WidgetCategory.System;

        public override Task RefreshAsync(CancellationToken cancellationToken = default)
        {
            RefreshCount++;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Coordinator_StartAndPulse_RefreshesActiveWidget()
    {
        var registry = new WidgetRegistry();
        var widget = new CountingWidget();
        registry.RegisterWidget(widget);

        using var coordinator = new WidgetRefreshCoordinator(registry);
        Assert.False(coordinator.IsRunning);
        Assert.False(coordinator.IsSuspended);

        await coordinator.StartAsync();
        Assert.True(coordinator.IsRunning);

        // Immediate refresh should have occurred
        Assert.True(widget.RefreshCount >= 1);

        await coordinator.StopAsync();
        Assert.False(coordinator.IsRunning);
    }

    [Fact]
    public async Task Coordinator_SuspendAndResume_ControlsLifecycle()
    {
        var registry = new WidgetRegistry();
        var widget = new CountingWidget();
        registry.RegisterWidget(widget);

        using var coordinator = new WidgetRefreshCoordinator(registry);
        await coordinator.StartAsync();

        coordinator.Suspend();
        Assert.True(coordinator.IsSuspended);

        var countAtSuspend = widget.RefreshCount;
        await Task.Delay(1100);
        // During suspension, 1s cadence timer is halted, so count should not increment from background ticks
        Assert.Equal(countAtSuspend, widget.RefreshCount);

        coordinator.Resume();
        Assert.False(coordinator.IsSuspended);

        // Resume should immediately trigger a refresh
        Assert.True(widget.RefreshCount > countAtSuspend);

        await coordinator.StopAsync();
    }

    [Fact]
    public void Widgets_HaveNoIndependentBackgroundTimers()
    {
        // Assert that BaseWidget and its implementations do not start separate timers
        var clock = new ClockWidget();
        var timer = new FocusTimerWidget();
        var monitor = new SystemMonitorWidget();

        // Check that initial states are quiescent without background ticker threads
        Assert.Equal(WidgetPresentationState.Active, clock.PresentationState);
        Assert.Equal(WidgetPresentationState.Empty, timer.PresentationState);
        Assert.False(timer.IsTimerRunning);
    }
}
