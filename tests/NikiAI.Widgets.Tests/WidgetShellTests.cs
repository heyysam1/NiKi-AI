using NikiAI.Core.Widgets;
using Xunit;

namespace NikiAI.Widgets.Tests;

public class WidgetShellTests
{
    private class DummyWidget : BaseWidget
    {
        public override string Id => "dummy";
        public override string Title => "Dummy Widget";
        public override WidgetCategory Category => WidgetCategory.System;

        public DummyWidget()
        {
            PrimaryDisplayValue = "42";
            SecondaryDisplayValue = "Metrics";
            ActionLabel = "Ping";
            PresentationState = WidgetPresentationState.Active;
        }

        public override Task ExecuteActionAsync(CancellationToken cancellationToken = default)
        {
            PrimaryDisplayValue = "43";
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void WidgetRegistry_RegisterAndRetrieve_Succeeds()
    {
        var registry = new WidgetRegistry();
        var widget = new DummyWidget();

        registry.RegisterWidget(widget);

        Assert.Equal(1, registry.Count);
        Assert.Same(widget, registry.GetWidget("dummy"));
        Assert.Same(widget, registry.GetWidget("DUMMY")); // Case-insensitive
        Assert.Single(registry.GetWidgetsByCategory(WidgetCategory.System));
        Assert.Empty(registry.GetWidgetsByCategory(WidgetCategory.Media));
    }

    [Fact]
    public void WidgetRegistry_Unregister_Succeeds()
    {
        var registry = new WidgetRegistry();
        var widget = new DummyWidget();
        registry.RegisterWidget(widget);

        var removed = registry.UnregisterWidget("dummy");
        Assert.True(removed);
        Assert.Equal(0, registry.Count);
        Assert.Null(registry.GetWidget("dummy"));
    }

    [Fact]
    public void WidgetViewModelBase_PropertiesAndSurfaceVariants_WorkProperly()
    {
        var widget = new DummyWidget();
        bool stateChanged = false;
        widget.StateChanged += (s, e) => stateChanged = true;

        Assert.Equal(WidgetSurfaceVariant.Glass, widget.SurfaceVariant);
        Assert.Equal(WidgetSizeOption.Standard, widget.SizeOption);
        Assert.True(widget.IsVisible);
        Assert.Equal("42", widget.PrimaryDisplayValue);

        widget.SurfaceVariant = WidgetSurfaceVariant.Solid;
        Assert.Equal(WidgetSurfaceVariant.Solid, widget.SurfaceVariant);
        Assert.True(stateChanged);

        stateChanged = false;
        widget.SurfaceVariant = WidgetSurfaceVariant.Minimal;
        Assert.Equal(WidgetSurfaceVariant.Minimal, widget.SurfaceVariant);
        Assert.True(stateChanged);
    }

    [Fact]
    public async Task WidgetViewModelBase_ExecuteAction_UpdatesState()
    {
        var widget = new DummyWidget();
        await widget.ExecuteActionAsync();
        Assert.Equal("43", widget.PrimaryDisplayValue);
    }
}
