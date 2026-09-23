using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using NikiAI.Core.Memory;

namespace NikiAI.Storage;

/// <summary>
/// SQLite-backed persistent repository for operational milestone timeline events.
/// Strictly an activity event log, isolated from explicit semantic long-term memory.
/// </summary>
public class SqliteTimelineRepository : ITimelineRepository
{
    private readonly StorageContext _context;
    private readonly ILogger<SqliteTimelineRepository>? _logger;

    public SqliteTimelineRepository(StorageContext context, ILogger<SqliteTimelineRepository>? logger = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger;
    }

    public async Task LogEventAsync(TimelineEvent timelineEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(timelineEvent);
        cancellationToken.ThrowIfCancellationRequested();

        using var connection = _context.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO timeline_events (id, event_type, source, summary, details_json, timestamp, related_id)
            VALUES ($id, $type, $src, $summary, $details, $ts, $relatedId);
        ";

        cmd.Parameters.AddWithValue("$id", timelineEvent.Id);
        cmd.Parameters.AddWithValue("$type", timelineEvent.EventType);
        cmd.Parameters.AddWithValue("$src", timelineEvent.Source);
        cmd.Parameters.AddWithValue("$summary", timelineEvent.Summary);
        cmd.Parameters.AddWithValue("$details", (object?)timelineEvent.DetailsJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ts", timelineEvent.Timestamp.ToString("O"));
        cmd.Parameters.AddWithValue("$relatedId", (object?)timelineEvent.RelatedId ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
        _logger?.LogDebug("Logged timeline event {EventId}: {Summary}", timelineEvent.Id, timelineEvent.Summary);
    }

    public async Task<IReadOnlyList<TimelineEvent>> GetRecentEventsAsync(int limit = 50, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var boundedLimit = Math.Clamp(limit, 1, 500);

        using var connection = _context.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT id, event_type, source, summary, details_json, timestamp, related_id
            FROM timeline_events
            ORDER BY timestamp DESC
            LIMIT $limit;
        ";
        cmd.Parameters.AddWithValue("$limit", boundedLimit);

        var list = new List<TimelineEvent>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(MapReaderToTimelineEvent(reader));
        }

        return list;
    }

    public async Task<IReadOnlyList<TimelineEvent>> GetEventsByRelatedIdAsync(string relatedId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relatedId);
        cancellationToken.ThrowIfCancellationRequested();

        using var connection = _context.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT id, event_type, source, summary, details_json, timestamp, related_id
            FROM timeline_events
            WHERE related_id = $relatedId
            ORDER BY timestamp DESC;
        ";
        cmd.Parameters.AddWithValue("$relatedId", relatedId);

        var list = new List<TimelineEvent>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(MapReaderToTimelineEvent(reader));
        }

        return list;
    }

    public async Task<int> ClearAllEventsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var connection = _context.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM timeline_events;";

        var rows = await cmd.ExecuteNonQueryAsync(cancellationToken);
        _logger?.LogInformation("Cleared all timeline events ({Count} rows)", rows);
        return rows;
    }

    private static TimelineEvent MapReaderToTimelineEvent(IDataRecord reader)
    {
        return new TimelineEvent(
            Id: reader.GetString(0),
            EventType: reader.GetString(1),
            Source: reader.GetString(2),
            Summary: reader.GetString(3),
            DetailsJson: reader.IsDBNull(4) ? null : reader.GetString(4),
            Timestamp: DateTimeOffset.Parse(reader.GetString(5)),
            RelatedId: reader.IsDBNull(6) ? null : reader.GetString(6)
        );
    }
}
