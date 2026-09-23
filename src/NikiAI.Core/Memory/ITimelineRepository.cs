namespace NikiAI.Core.Memory;

/// <summary>
/// Domain model representing an immutable, operational milestone event in the application activity timeline.
/// Strictly distinct from explicit long-term memory.
/// </summary>
public record TimelineEvent(
    string Id,
    string EventType,
    string Source,
    string Summary,
    string? DetailsJson,
    DateTimeOffset Timestamp,
    string? RelatedId = null
);

/// <summary>
/// Repository contract for logging and retrieving chronological application timeline events.
/// </summary>
public interface ITimelineRepository
{
    Task LogEventAsync(TimelineEvent timelineEvent, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TimelineEvent>> GetRecentEventsAsync(int limit = 50, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TimelineEvent>> GetEventsByRelatedIdAsync(string relatedId, CancellationToken cancellationToken = default);
    Task<int> ClearAllEventsAsync(CancellationToken cancellationToken = default);
}
