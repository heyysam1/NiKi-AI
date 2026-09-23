using System.Data;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using NikiAI.Core.Notifications;

namespace NikiAI.Storage;

/// <summary>
/// SQLite-backed persistence repository for notification history.
/// Saves notifications, queries history, and supports clear operations.
/// </summary>
public class SqliteNotificationRepository : INotificationRepository
{
    private readonly StorageContext _storageContext;
    private readonly ILogger<SqliteNotificationRepository>? _logger;

    public SqliteNotificationRepository(StorageContext storageContext, ILogger<SqliteNotificationRepository>? logger = null)
    {
        _storageContext = storageContext ?? throw new ArgumentNullException(nameof(storageContext));
        _logger = logger;
    }

    public async Task SaveAsync(NotificationPayload notification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);

        using var connection = _storageContext.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO notifications (
                id, title, summary_message, notification_type, completion_state,
                one_sentence_summary, associated_task_id, show_popup,
                key_outputs_json, artifact_links_json, character_avatar_path,
                created_at, is_read, is_dismissed
            )
            VALUES (
                $id, $title, $summary_message, $notification_type, $completion_state,
                $one_sentence_summary, $associated_task_id, $show_popup,
                $key_outputs_json, $artifact_links_json, $character_avatar_path,
                $created_at, 0, 0
            )
            ON CONFLICT(id) DO UPDATE SET
                title = excluded.title,
                summary_message = excluded.summary_message,
                notification_type = excluded.notification_type,
                completion_state = excluded.completion_state,
                one_sentence_summary = excluded.one_sentence_summary,
                show_popup = excluded.show_popup,
                key_outputs_json = excluded.key_outputs_json,
                artifact_links_json = excluded.artifact_links_json,
                character_avatar_path = excluded.character_avatar_path;
        ";

        cmd.Parameters.AddWithValue("$id", notification.NotificationId);
        cmd.Parameters.AddWithValue("$title", notification.Title);
        cmd.Parameters.AddWithValue("$summary_message", notification.SummaryMessage);
        cmd.Parameters.AddWithValue("$notification_type", (int)notification.Type);
        cmd.Parameters.AddWithValue("$completion_state", (int)notification.CompletionState);
        cmd.Parameters.AddWithValue("$one_sentence_summary", (object?)notification.OneSentenceSummary ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$associated_task_id", (object?)notification.AssociatedTaskId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$show_popup", notification.ShowPopup ? 1 : 0);
        cmd.Parameters.AddWithValue("$key_outputs_json", notification.KeyOutputs != null ? JsonSerializer.Serialize(notification.KeyOutputs) : DBNull.Value);
        cmd.Parameters.AddWithValue("$artifact_links_json", notification.ArtifactLinks != null ? JsonSerializer.Serialize(notification.ArtifactLinks) : DBNull.Value);
        cmd.Parameters.AddWithValue("$character_avatar_path", (object?)notification.CharacterAvatarPath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$created_at", notification.CreatedAt.ToString("O"));

        await cmd.ExecuteNonQueryAsync(cancellationToken);
        _logger?.LogDebug("Saved notification {Id} ('{Title}') to history.", notification.NotificationId, notification.Title);
    }

    public async Task<IReadOnlyList<NotificationPayload>> GetHistoryAsync(int limit = 50, CancellationToken cancellationToken = default)
    {
        using var connection = _storageContext.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT id, title, summary_message, notification_type, completion_state,
                   one_sentence_summary, associated_task_id, show_popup,
                   key_outputs_json, artifact_links_json, character_avatar_path, created_at
            FROM notifications
            ORDER BY created_at DESC
            LIMIT $limit;
        ";
        cmd.Parameters.AddWithValue("$limit", Math.Max(1, limit));

        var list = new List<NotificationPayload>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetString(0);
            var title = reader.GetString(1);
            var summaryMessage = reader.GetString(2);
            var type = (NotificationType)reader.GetInt32(3);
            var completionState = (TaskCompletionState)reader.GetInt32(4);
            var oneSentenceSummary = reader.IsDBNull(5) ? summaryMessage : reader.GetString(5);
            var associatedTaskId = reader.IsDBNull(6) ? null : reader.GetString(6);
            var showPopup = reader.GetInt32(7) == 1;

            IReadOnlyList<string>? keyOutputs = null;
            if (!reader.IsDBNull(8))
            {
                try { keyOutputs = JsonSerializer.Deserialize<List<string>>(reader.GetString(8)); } catch { }
            }

            IReadOnlyList<string>? artifactLinks = null;
            if (!reader.IsDBNull(9))
            {
                try { artifactLinks = JsonSerializer.Deserialize<List<string>>(reader.GetString(9)); } catch { }
            }

            var characterAvatarPath = reader.IsDBNull(10) ? null : reader.GetString(10);
            var createdAt = DateTimeOffset.Parse(reader.GetString(11));

            list.Add(new NotificationPayload(
                id,
                title,
                summaryMessage,
                associatedTaskId,
                showPopup,
                type,
                completionState,
                oneSentenceSummary,
                keyOutputs,
                artifactLinks,
                characterAvatarPath,
                createdAt
            ));
        }

        return list;
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _storageContext.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM notifications;";
        await cmd.ExecuteNonQueryAsync(cancellationToken);
        _logger?.LogInformation("Cleared all notifications from history.");
    }
}
