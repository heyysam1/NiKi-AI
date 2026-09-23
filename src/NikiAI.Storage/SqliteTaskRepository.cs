using System.Data;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using NikiAI.Core.Tasks;

namespace NikiAI.Storage;

/// <summary>
/// Production SQLite implementation of ITaskRepository.
/// Enforces parameterized queries, atomic state+event transactions, and soft-delete audit preservation.
/// </summary>
public class SqliteTaskRepository : ITaskRepository
{
    private readonly StorageContext _storageContext;
    private readonly ILogger<SqliteTaskRepository>? _logger;

    public event Action<string, AgentTaskStatus, AgentTaskStatus>? StatusChanged;

    public SqliteTaskRepository(StorageContext storageContext, ILogger<SqliteTaskRepository>? logger = null)
    {
        _storageContext = storageContext ?? throw new ArgumentNullException(nameof(storageContext));
        _logger = logger;
    }

    public async Task<AgentTask> CreateAsync(AgentTask task, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);

        using var connection = _storageContext.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        try
        {
            using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = @"
                INSERT INTO tasks (
                    id, title, natural_language_request, structured_goal,
                    priority, status, progress_percentage, created_at,
                    started_at, due_at, completed_at, actions_json,
                    result_summary, error_information, artifact_links_json, is_archived
                ) VALUES (
                    $id, $title, $request, $goal,
                    $priority, $status, $progress, $createdAt,
                    $startedAt, $dueAt, $completedAt, $actionsJson,
                    $result, $error, $artifactsJson, $isArchived
                );
            ";

            BindTaskParameters(cmd, task);
            await cmd.ExecuteNonQueryAsync(cancellationToken);

            // Record initial creation audit event
            using var eventCmd = connection.CreateCommand();
            eventCmd.Transaction = transaction;
            eventCmd.CommandText = @"
                INSERT INTO task_events (id, task_id, timestamp, event_type, message, details_json)
                VALUES ($eventId, $taskId, $timestamp, $eventType, $message, $detailsJson);
            ";
            eventCmd.Parameters.AddWithValue("$eventId", Guid.NewGuid().ToString("N"));
            eventCmd.Parameters.AddWithValue("$taskId", task.Id);
            eventCmd.Parameters.AddWithValue("$timestamp", DateTimeOffset.UtcNow.ToString("O"));
            eventCmd.Parameters.AddWithValue("$eventType", (int)TaskEventType.Created);
            eventCmd.Parameters.AddWithValue("$message", $"Task created: '{task.Title}' with status '{task.Status}'.");
            eventCmd.Parameters.AddWithValue("$detailsJson", DBNull.Value);
            await eventCmd.ExecuteNonQueryAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            _logger?.LogInformation("Created task {Id} ('{Title}')", task.Id, task.Title);
            return task;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger?.LogError(ex, "Failed to create task {Id}", task.Id);
            throw;
        }
    }

    public async Task<AgentTask?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;

        using var connection = _storageContext.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM tasks WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return MapTask(reader);
        }

        return null;
    }

    public async Task<IReadOnlyList<AgentTask>> GetAllAsync(
        bool includeArchived = false,
        int limit = 100,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        using var connection = _storageContext.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = includeArchived
            ? "SELECT * FROM tasks ORDER BY created_at DESC LIMIT $limit OFFSET $offset;"
            : "SELECT * FROM tasks WHERE is_archived = 0 ORDER BY created_at DESC LIMIT $limit OFFSET $offset;";

        cmd.Parameters.AddWithValue("$limit", Math.Max(1, limit));
        cmd.Parameters.AddWithValue("$offset", Math.Max(0, offset));

        var list = new List<AgentTask>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(MapTask(reader));
        }

        return list.AsReadOnly();
    }

    public async Task<IReadOnlyList<AgentTask>> GetByStatusAsync(
        AgentTaskStatus status,
        bool includeArchived = false,
        CancellationToken cancellationToken = default)
    {
        using var connection = _storageContext.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = includeArchived
            ? "SELECT * FROM tasks WHERE status = $status ORDER BY created_at DESC;"
            : "SELECT * FROM tasks WHERE status = $status AND is_archived = 0 ORDER BY created_at DESC;";

        cmd.Parameters.AddWithValue("$status", (int)status);

        var list = new List<AgentTask>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(MapTask(reader));
        }

        return list.AsReadOnly();
    }

    public async Task UpdateAsync(AgentTask task, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);

        using var connection = _storageContext.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            UPDATE tasks SET
                title = $title,
                natural_language_request = $request,
                structured_goal = $goal,
                priority = $priority,
                status = $status,
                progress_percentage = $progress,
                started_at = $startedAt,
                due_at = $dueAt,
                completed_at = $completedAt,
                actions_json = $actionsJson,
                result_summary = $result,
                error_information = $error,
                artifact_links_json = $artifactsJson,
                is_archived = $isArchived
            WHERE id = $id;
        ";

        BindTaskParameters(cmd, task);
        var rowsAffected = await cmd.ExecuteNonQueryAsync(cancellationToken);
        if (rowsAffected == 0)
        {
            throw new KeyNotFoundException($"Task with ID '{task.Id}' was not found for update.");
        }
    }

    public async Task TransitionStatusAsync(
        string taskId,
        AgentTaskStatus newStatus,
        string? message = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(taskId)) throw new ArgumentException("Task ID cannot be empty.", nameof(taskId));

        using var connection = _storageContext.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        try
        {
            // 1. Fetch current task to validate transition
            AgentTask? task;
            using (var selectCmd = connection.CreateCommand())
            {
                selectCmd.Transaction = transaction;
                selectCmd.CommandText = "SELECT * FROM tasks WHERE id = $id;";
                selectCmd.Parameters.AddWithValue("$id", taskId);
                using var reader = await selectCmd.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                {
                    throw new KeyNotFoundException($"Task '{taskId}' was not found.");
                }
                task = MapTask(reader);
            }

            var previousStatus = task.Status;
            task.TransitionTo(newStatus); // Enforces state machine rules and sets timestamps

            // 2. Atomically update task in DB
            using (var updateCmd = connection.CreateCommand())
            {
                updateCmd.Transaction = transaction;
                updateCmd.CommandText = @"
                    UPDATE tasks SET
                        status = $status,
                        started_at = $startedAt,
                        completed_at = $completedAt
                    WHERE id = $id;
                ";
                updateCmd.Parameters.AddWithValue("$status", (int)task.Status);
                updateCmd.Parameters.AddWithValue("$startedAt", task.StartedAt.HasValue ? task.StartedAt.Value.ToString("O") : DBNull.Value);
                updateCmd.Parameters.AddWithValue("$completedAt", task.CompletedAt.HasValue ? task.CompletedAt.Value.ToString("O") : DBNull.Value);
                updateCmd.Parameters.AddWithValue("$id", task.Id);
                await updateCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            // 3. Atomically insert transition audit event
            var eventMsg = !string.IsNullOrWhiteSpace(message)
                ? message
                : $"Task transitioned from {previousStatus} to {newStatus}.";

            using (var eventCmd = connection.CreateCommand())
            {
                eventCmd.Transaction = transaction;
                eventCmd.CommandText = @"
                    INSERT INTO task_events (id, task_id, timestamp, event_type, message, details_json)
                    VALUES ($eventId, $taskId, $timestamp, $eventType, $message, $detailsJson);
                ";
                eventCmd.Parameters.AddWithValue("$eventId", Guid.NewGuid().ToString("N"));
                eventCmd.Parameters.AddWithValue("$taskId", task.Id);
                eventCmd.Parameters.AddWithValue("$timestamp", DateTimeOffset.UtcNow.ToString("O"));
                eventCmd.Parameters.AddWithValue("$eventType", (int)TaskEventType.StatusChanged);
                eventCmd.Parameters.AddWithValue("$message", eventMsg);
                eventCmd.Parameters.AddWithValue("$detailsJson", JsonSerializer.Serialize(new { from = previousStatus.ToString(), to = newStatus.ToString() }));
                await eventCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            _logger?.LogInformation("Task {Id} atomically transitioned to {Status}", taskId, newStatus);
            StatusChanged?.Invoke(taskId, previousStatus, newStatus);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger?.LogError(ex, "Failed to transition status for task {Id}", taskId);
            throw;
        }
    }

    public async Task ArchiveAsync(string taskId, string? reason = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(taskId)) throw new ArgumentException("Task ID cannot be empty.", nameof(taskId));

        using var connection = _storageContext.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        try
        {
            using (var updateCmd = connection.CreateCommand())
            {
                updateCmd.Transaction = transaction;
                updateCmd.CommandText = "UPDATE tasks SET is_archived = 1 WHERE id = $id;";
                updateCmd.Parameters.AddWithValue("$id", taskId);
                var rows = await updateCmd.ExecuteNonQueryAsync(cancellationToken);
                if (rows == 0)
                {
                    throw new KeyNotFoundException($"Task '{taskId}' was not found for archiving.");
                }
            }

            using (var eventCmd = connection.CreateCommand())
            {
                eventCmd.Transaction = transaction;
                eventCmd.CommandText = @"
                    INSERT INTO task_events (id, task_id, timestamp, event_type, message, details_json)
                    VALUES ($eventId, $taskId, $timestamp, $eventType, $message, $detailsJson);
                ";
                eventCmd.Parameters.AddWithValue("$eventId", Guid.NewGuid().ToString("N"));
                eventCmd.Parameters.AddWithValue("$taskId", taskId);
                eventCmd.Parameters.AddWithValue("$timestamp", DateTimeOffset.UtcNow.ToString("O"));
                eventCmd.Parameters.AddWithValue("$eventType", (int)TaskEventType.Archived);
                eventCmd.Parameters.AddWithValue("$message", reason ?? "Task archived by user.");
                eventCmd.Parameters.AddWithValue("$detailsJson", DBNull.Value);
                await eventCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            _logger?.LogInformation("Task {Id} successfully archived", taskId);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger?.LogError(ex, "Failed to archive task {Id}", taskId);
            throw;
        }
    }

    public async Task PurgePermanentlyAsync(string taskId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(taskId)) throw new ArgumentException("Task ID cannot be empty.", nameof(taskId));

        _logger?.LogWarning("Executing destructive permanent purge for task {Id}", taskId);

        using var connection = _storageContext.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        try
        {
            // Explicitly delete events first
            using (var delEventsCmd = connection.CreateCommand())
            {
                delEventsCmd.Transaction = transaction;
                delEventsCmd.CommandText = "DELETE FROM task_events WHERE task_id = $id;";
                delEventsCmd.Parameters.AddWithValue("$id", taskId);
                await delEventsCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            // Explicitly delete task
            using (var delTaskCmd = connection.CreateCommand())
            {
                delTaskCmd.Transaction = transaction;
                delTaskCmd.CommandText = "DELETE FROM tasks WHERE id = $id;";
                delTaskCmd.Parameters.AddWithValue("$id", taskId);
                var rows = await delTaskCmd.ExecuteNonQueryAsync(cancellationToken);
                if (rows == 0)
                {
                    throw new KeyNotFoundException($"Task '{taskId}' was not found for purge.");
                }
            }

            await transaction.CommitAsync(cancellationToken);
            _logger?.LogInformation("Task {Id} and associated events permanently purged.", taskId);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger?.LogError(ex, "Failed to permanently purge task {Id}", taskId);
            throw;
        }
    }

    public async Task AddEventAsync(TaskEvent taskEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(taskEvent);

        using var connection = _storageContext.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO task_events (id, task_id, timestamp, event_type, message, details_json)
            VALUES ($id, $taskId, $timestamp, $eventType, $message, $detailsJson);
        ";
        cmd.Parameters.AddWithValue("$id", taskEvent.Id);
        cmd.Parameters.AddWithValue("$taskId", taskEvent.TaskId);
        cmd.Parameters.AddWithValue("$timestamp", taskEvent.Timestamp.ToString("O"));
        cmd.Parameters.AddWithValue("$eventType", (int)taskEvent.EventType);
        cmd.Parameters.AddWithValue("$message", taskEvent.Message);
        cmd.Parameters.AddWithValue("$detailsJson", (object?)taskEvent.DetailsJson ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TaskEvent>> GetEventsAsync(string taskId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(taskId)) return Array.Empty<TaskEvent>();

        using var connection = _storageContext.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM task_events WHERE task_id = $taskId ORDER BY timestamp ASC;";
        cmd.Parameters.AddWithValue("$taskId", taskId);

        var events = new List<TaskEvent>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            events.Add(new TaskEvent
            {
                Id = reader.GetString(reader.GetOrdinal("id")),
                TaskId = reader.GetString(reader.GetOrdinal("task_id")),
                Timestamp = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("timestamp"))),
                EventType = (TaskEventType)reader.GetInt32(reader.GetOrdinal("event_type")),
                Message = reader.GetString(reader.GetOrdinal("message")),
                DetailsJson = reader.IsDBNull(reader.GetOrdinal("details_json")) ? null : reader.GetString(reader.GetOrdinal("details_json"))
            });
        }

        return events.AsReadOnly();
    }

    private static void BindTaskParameters(SqliteCommand cmd, AgentTask task)
    {
        cmd.Parameters.AddWithValue("$id", task.Id);
        cmd.Parameters.AddWithValue("$title", task.Title);
        cmd.Parameters.AddWithValue("$request", (object?)task.NaturalLanguageRequest ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$goal", (object?)task.StructuredGoal ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$priority", (int)task.Priority);
        cmd.Parameters.AddWithValue("$status", (int)task.Status);
        cmd.Parameters.AddWithValue("$progress", (object?)task.ProgressPercentage ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$createdAt", task.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$startedAt", task.StartedAt.HasValue ? task.StartedAt.Value.ToString("O") : DBNull.Value);
        cmd.Parameters.AddWithValue("$dueAt", task.DueAt.HasValue ? task.DueAt.Value.ToString("O") : DBNull.Value);
        cmd.Parameters.AddWithValue("$completedAt", task.CompletedAt.HasValue ? task.CompletedAt.Value.ToString("O") : DBNull.Value);
        cmd.Parameters.AddWithValue("$actionsJson", JsonSerializer.Serialize(task.ActionsTaken));
        cmd.Parameters.AddWithValue("$result", (object?)task.ResultSummary ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$error", (object?)task.ErrorInformation ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$artifactsJson", JsonSerializer.Serialize(task.ArtifactLinks));
        cmd.Parameters.AddWithValue("$isArchived", task.IsArchived ? 1 : 0);
    }

    private static AgentTask MapTask(SqliteDataReader reader)
    {
        var task = new AgentTask
        {
            Id = reader.GetString(reader.GetOrdinal("id")),
            Title = reader.GetString(reader.GetOrdinal("title")),
            NaturalLanguageRequest = reader.IsDBNull(reader.GetOrdinal("natural_language_request")) ? string.Empty : reader.GetString(reader.GetOrdinal("natural_language_request")),
            StructuredGoal = reader.IsDBNull(reader.GetOrdinal("structured_goal")) ? string.Empty : reader.GetString(reader.GetOrdinal("structured_goal")),
            Priority = (AgentTaskPriority)reader.GetInt32(reader.GetOrdinal("priority")),
            Status = (AgentTaskStatus)reader.GetInt32(reader.GetOrdinal("status")),
            ProgressPercentage = reader.IsDBNull(reader.GetOrdinal("progress_percentage")) ? null : reader.GetInt32(reader.GetOrdinal("progress_percentage")),
            CreatedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("created_at"))),
            StartedAt = reader.IsDBNull(reader.GetOrdinal("started_at")) ? null : DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("started_at"))),
            DueAt = reader.IsDBNull(reader.GetOrdinal("due_at")) ? null : DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("due_at"))),
            CompletedAt = reader.IsDBNull(reader.GetOrdinal("completed_at")) ? null : DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("completed_at"))),
            ResultSummary = reader.IsDBNull(reader.GetOrdinal("result_summary")) ? null : reader.GetString(reader.GetOrdinal("result_summary")),
            ErrorInformation = reader.IsDBNull(reader.GetOrdinal("error_information")) ? null : reader.GetString(reader.GetOrdinal("error_information")),
            IsArchived = reader.GetInt32(reader.GetOrdinal("is_archived")) == 1
        };

        if (!reader.IsDBNull(reader.GetOrdinal("actions_json")))
        {
            var actions = JsonSerializer.Deserialize<List<string>>(reader.GetString(reader.GetOrdinal("actions_json")));
            if (actions != null) task.ActionsTaken.AddRange(actions);
        }

        if (!reader.IsDBNull(reader.GetOrdinal("artifact_links_json")))
        {
            var artifacts = JsonSerializer.Deserialize<List<string>>(reader.GetString(reader.GetOrdinal("artifact_links_json")));
            if (artifacts != null) task.ArtifactLinks.AddRange(artifacts);
        }

        return task;
    }
}
