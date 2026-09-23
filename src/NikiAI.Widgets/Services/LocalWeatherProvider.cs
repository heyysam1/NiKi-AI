using NikiAI.Core.Widgets;

namespace NikiAI.Widgets.Services;

/// <summary>
/// Default safe local/offline weather provider.
/// Does not assume or introduce external network APIs without specifications.
/// Reports a valid unavailable/offline state.
/// </summary>
public class LocalWeatherProvider : IWeatherProvider
{
    public Task<WeatherReport> GetCurrentWeatherAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var report = new WeatherReport(
            Condition: "Unavailable",
            Temperature: "--",
            Location: "Local Desktop",
            HighLow: "Offline mode",
            IsAvailable: false
        );

        return Task.FromResult(report);
    }
}
