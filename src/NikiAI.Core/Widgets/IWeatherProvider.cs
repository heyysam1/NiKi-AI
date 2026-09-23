namespace NikiAI.Core.Widgets;

/// <summary>
/// Weather condition report data model.
/// </summary>
public record WeatherReport(
    string Condition,
    string Temperature,
    string Location,
    string HighLow,
    bool IsAvailable
);

/// <summary>
/// Abstraction for weather data providers.
/// Default implementation provides offline/unavailable state without requiring external network APIs.
/// </summary>
public interface IWeatherProvider
{
    Task<WeatherReport> GetCurrentWeatherAsync(CancellationToken cancellationToken = default);
}
