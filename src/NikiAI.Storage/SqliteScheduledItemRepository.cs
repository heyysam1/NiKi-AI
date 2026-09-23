using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using NikiAI.Core.Scheduler;

namespace NikiAI.Storage;

/// <summary>
/// SQLite-backed persistence repository for ScheduledItem records.
/// Ensures scheduled items survive application restarts and handles concurrency safely.
/// </summary>
public class SqliteScheduledItemRepository : IScheduledItemRepository
{
    private readonly StorageContext _storageContext;
    private readonly ILogger<SqliteScheduledItemRepository>? _logger;

    public SqliteScheduledItemRepository(StorageContext storageContext, ILogger<SqliteScheduledItemRepository>? logger = null)
    {
        _storageContext = storageContext ?? throw new ArgumentNullException(nameof(storageContext));
        _logger = logger;
    }

    public async Task<ScheduledItem> CreateOrUpdateAsync(ScheduledItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        using var connection = _storageContext.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO scheduled_items (
                id, title, description, scheduled_time, is_recurring, recurrence_interval_ticks,
                associated_task_id, item_type, status, payload_json, created_at, updated_at
            )
            VALUES (
                $id, $title, $description, $scheduled_time, $is_recurring, $recurrence_interval_ticks,
                $associated_task_id, $item_type, $status, $payload_json, $created_at, $updated_at
            )
            ON CONFLICT(id) DO UPDATE SET
                title = excluded.title,
                description = excluded.description,
                scheduled_time = excluded.scheduled_time,
                is_recurring = excluded.is_recurring,
                recurrence_interval_ticks = excluded.recurrence_interval_ticks,
                associated_task_id = excluded.associated_task_id,
                item_type = excluded.item_type,
                status = excluded.status,
                payload_json = excluded.payload_json,
                updated_at = excluded.updated_at;
        ";

        cmd.Parameters.AddWithValue("$id", item.Id);
        cmd.Parameters.AddWithValue("$title", item.Title);
        cmd.Parameters.AddWithValue("$description", (object?)item.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$scheduled_time", item.ScheduledTime.ToString("O"));
        cmd.Parameters.AddWithValue("$is_recurring", item.IsRecurring ? 1 : 0);
        cmd.Parameters.AddWithValue("$recurrence_interval_ticks", item.RecurrenceInterval.HasValue ? (object)item.RecurrenceInterval.Value.Ticks : DBNull.Value);
        cmd.Parameters.AddWithValue("$associated_task_id", (object?)item.AssociatedTaskId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$item_type", (int)item.ItemType);
        cmd.Parameters.AddWithValue("$status", (int)item.Status);
        cmd.Parameters.AddWithValue("$payload_json", (object?)item.PayloadJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$created_at", item.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$updated_at", DateTimeOffset.UtcNow.ToString("O"));

        await cmd.ExecuteNonQueryAsync(cancellationToken);
        _logger?.LogDebug("Saved scheduled item {Id} ('{Title}') due at {ScheduledTime}", item.Id, item.Title, item.ScheduledTime);
        return item;
    }

    public async Task<ScheduledItem?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        using var connection = _storageContext.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT id, title, description, scheduled_time, is_recurring, recurrence_interval_ticks,
                   associated_task_id, item_type, status, payload_json, created_at, updated_at
            FROM scheduled_items
            WHERE id = $id;
        ";
        cmd.Parameters.AddWithValue("$id", id);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return MapFromReader(reader);
        }

        return null;
    }

    public async Task<IReadOnlyList<ScheduledItem>> GetPendingDueItemsAsync(DateTimeOffset asOfTime, CancellationToken cancellationToken = default)
    {
        using var connection = _storageContext.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT id, title, description, scheduled_time, is_recurring, recurrence_interval_ticks,
                   associated_task_id, item_type, status, payload_json, created_at, updated_at
            FROM scheduled_items
            WHERE status IN (0, 4) AND scheduled_time <= $as_of_time
            ORDER BY scheduled_time ASC;
        ";
        cmd.Parameters.AddWithValue("$as_of_time", asOfTime.ToString("O"));

        var list = new List<ScheduledItem>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(MapFromReader(reader));
        }

        return list;
    }

    public async Task<IReadOnlyList<ScheduledItem>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _storageContext.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT id, title, description, scheduled_time, is_recurring, recurrence_interval_ticks,
                   associated_task_id, item_type, status, payload_json, created_at, updated_at
            FROM scheduled_items
            ORDER BY scheduled_time ASC;
        ";

        var list = new List<ScheduledItem>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(MapFromReader(reader));
        }

        return list;
    }

    public async Task UpdateStatusAsync(string id, ScheduledItemStatus status, DateTimeOffset? nextTime = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        using var connection = _storageContext.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        if (nextTime.HasValue)
        {
            cmd.CommandText = @"
                UPDATE scheduled_items
                SET status = $status,
                    scheduled_time = $scheduled_time,
                    updated_at = $updated_at
                WHERE id = $id;
            ";
            cmd.Parameters.AddWithValue("$scheduled_time", nextTime.Value.ToString("O"));
        }
        else
        {
            cmd.CommandText = @"
                UPDATE scheduled_items
                SET status = $status,
                    updated_at = $updated_at
                WHERE id = $id;
            ";
        }

        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$status", (int)status);
        cmd.Parameters.AddWithValue("$updated_at", DateTimeOffset.UtcNow.ToString("O"));

        await cmd.ExecuteNonQueryAsync(cancellationToken);
        _logger?.LogDebug("Updated status for item {Id} to {Status}", id, status);
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        using var connection = _storageContext.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM scheduled_items WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
        _logger?.LogDebug("Deleted scheduled item {Id}", id);
    }

    private static ScheduledItem MapFromReader(SqliteDataReader reader)
    {
        var id = reader.GetString(0);
        var title = reader.GetString(1);
        var description = reader.IsDBNull(2) ? null : reader.GetString(2);
        var scheduledTime = DateTimeOffset.Parse(reader.GetString(3));
        var isRecurring = reader.GetInt32(4) == 1;
        TimeSpan? recurrenceInterval = reader.IsDBNull(5) ? null : TimeSpan.FromTicks(reader.GetInt64(5));
        var associatedTaskId = reader.IsDBNull(6) ? null : reader.GetString(6);
        var itemType = (ScheduledItemType)reader.GetInt32(7);
        var status = (ScheduledItemStatus)reader.GetInt32(8);
        var payloadJson = reader.IsDBNull(9) ? null : reader.GetString(9);
        var createdAt = DateTimeOffset.Parse(reader.GetString(10));
        var updatedAt = DateTimeOffset.Parse(reader.GetString(11));

        return new ScheduledItem(
            id,
            title,
            scheduledTime,
            isRecurring,
            recurrenceInterval,
            associatedTaskId,
            description,
            itemType,
            status,
            payloadJson,
            createdAt,
            updatedAt
        );
    }
}
