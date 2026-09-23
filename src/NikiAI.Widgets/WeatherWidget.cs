using NikiAI.Core.Widgets;

namespace NikiAI.Widgets;

/// <summary>
/// Weather Widget: Displays local weather condition and temperature.
/// Consumes IWeatherProvider with safe offline fallback.
/// Correctly produces a valid Unavailable UI state when offline or unconfigured.
/// </summary>
public class WeatherWidget : BaseWidget
{
    private readonly IWeatherProvider _weatherProvider;

    public override string Id => "weather";
    public override string Title => "Weather";
    public override WidgetCategory Category => WidgetCategory.Information;
    public override string IconGlyph => "🌤";

    public WeatherWidget(IWeatherProvider weatherProvider)
    {
        _weatherProvider = weatherProvider ?? throw new ArgumentNullException(nameof(weatherProvider));
        ActionLabel = "Refresh";
        PrimaryDisplayValue = "Weather unavailable";
        SecondaryDisplayValue = "Offline mode";
        PresentationState = WidgetPresentationState.Unavailable;
    }

    public override async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        try
        {
            var report = await _weatherProvider.GetCurrentWeatherAsync(cancellationToken).ConfigureAwait(false);
            if (report != null && report.IsAvailable)
            {
                PrimaryDisplayValue = $"{report.Temperature} {report.Condition}";
                SecondaryDisplayValue = $"{report.Location} ({report.HighLow})";
                PresentationState = WidgetPresentationState.Active;
            }
            else
            {
                PrimaryDisplayValue = "Weather unavailable";
                SecondaryDisplayValue = report?.HighLow ?? "Offline mode";
                PresentationState = WidgetPresentationState.Unavailable;
            }

            HasError = false;
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = ex.Message;
            PrimaryDisplayValue = "Weather unavailable";
            PresentationState = WidgetPresentationState.Unavailable;
        }
        finally
        {
            IsLoading = false;
        }
    }

    public override Task ExecuteActionAsync(CancellationToken cancellationToken = default)
    {
        return RefreshAsync(cancellationToken);
    }
}
