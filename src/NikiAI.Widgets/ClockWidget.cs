using NikiAI.Core.Widgets;

namespace NikiAI.Widgets;

/// <summary>
/// Clock Widget: Displays local time and current date.
/// Small action toggles between 12-hour (AM/PM) and 24-hour formats.
/// Uses centralized 1-second pulse without independent polling loops.
/// </summary>
public class ClockWidget : BaseWidget
{
    private bool _use24HourFormat;

    public override string Id => "clock";
    public override string Title => "Clock";
    public override WidgetCategory Category => WidgetCategory.System;
    public override string IconGlyph => "🕒";

    public bool Use24HourFormat
    {
        get => _use24HourFormat;
        set
        {
            if (SetField(ref _use24HourFormat, value))
            {
                UpdateDisplay();
            }
        }
    }

    public ClockWidget()
    {
        ActionLabel = "12h/24h";
        UpdateDisplay();
    }

    public override Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        UpdateDisplay();
        return Task.CompletedTask;
    }

    public override Task ExecuteActionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Use24HourFormat = !Use24HourFormat;
        return Task.CompletedTask;
    }

    private void UpdateDisplay()
    {
        var now = DateTime.Now;
        PrimaryDisplayValue = _use24HourFormat
            ? now.ToString("HH:mm:ss")
            : now.ToString("h:mm:ss tt");

        SecondaryDisplayValue = now.ToString("dddd, MMM d");
        PresentationState = WidgetPresentationState.Active;
    }
}
