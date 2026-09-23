using System.Collections.Concurrent;
using NikiAI.Core.Memory;

namespace NikiAI.Memory;

public class InMemoryTimelineRepository : ITimelineRepository
{
    private readonly ConcurrentBag<TimelineEvent> _events = new();

    public Task LogEventAsync(TimelineEvent timelineEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(timelineEvent);
        _events.Add(timelineEvent);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<TimelineEvent>> GetRecentEventsAsync(int limit = 50, CancellationToken cancellationToken = default)
    {
        var list = _events.OrderByDescending(e => e.Timestamp).Take(limit).ToList();
        return Task.FromResult<IReadOnlyList<TimelineEvent>>(list);
    }

    public Task<IReadOnlyList<TimelineEvent>> GetEventsByRelatedIdAsync(string relatedId, CancellationToken cancellationToken = default)
    {
        var list = _events.Where(e => e.RelatedId == relatedId).OrderByDescending(e => e.Timestamp).ToList();
        return Task.FromResult<IReadOnlyList<TimelineEvent>>(list);
    }

    public Task<int> ClearAllEventsAsync(CancellationToken cancellationToken = default)
    {
        var count = _events.Count;
        _events.Clear();
        return Task.FromResult(count);
    }
}
