using System.Data;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using NikiAI.Core.Workflows;

namespace NikiAI.Storage;

/// <summary>
/// SQLite implementation of IWorkflowRepository.
/// Enforces operational-only persistence for workflow runs (zero sensitive tool arguments/results/payloads).
/// </summary>
public class SqliteWorkflowRepository : IWorkflowRepository
{
    private readonly StorageContext _storageContext;
    private readonly ILogger<SqliteWorkflowRepository>? _logger;

    public SqliteWorkflowRepository(StorageContext storageContext, ILogger<SqliteWorkflowRepository>? logger = null)
    {
        _storageContext = storageContext ?? throw new ArgumentNullException(nameof(storageContext));
        _logger = logger;
    }

    public async Task<WorkflowDefinition> CreateWorkflowAsync(WorkflowDefinition workflow, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        using var connection = _storageContext.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO workflows (
                id, name, description, category, trigger_type,
                trigger_config_json, inputs_json, actions_json,
                timeout_seconds, completion_behavior, created_at, updated_at, is_enabled
            ) VALUES (
                $id, $name, $description, $category, $triggerType,
                $triggerConfig, $inputs, $actions,
                $timeout, $completionBehavior, $createdAt, $updatedAt, $isEnabled
            );
        ";

        cmd.Parameters.AddWithValue("$id", workflow.Id);
        cmd.Parameters.AddWithValue("$name", workflow.Name);
        cmd.Parameters.AddWithValue("$description", (object?)workflow.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$category", workflow.Category);
        cmd.Parameters.AddWithValue("$triggerType", (int)workflow.Trigger);
        cmd.Parameters.AddWithValue("$triggerConfig", (object?)workflow.TriggerConfigJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$inputs", JsonSerializer.Serialize(workflow.Inputs));
        cmd.Parameters.AddWithValue("$actions", JsonSerializer.Serialize(workflow.Actions));
        cmd.Parameters.AddWithValue("$timeout", workflow.TimeoutSeconds);
        cmd.Parameters.AddWithValue("$completionBehavior", (int)workflow.CompletionBehavior);
        cmd.Parameters.AddWithValue("$createdAt", workflow.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$updatedAt", workflow.UpdatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$isEnabled", workflow.IsEnabled ? 1 : 0);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
        _logger?.LogInformation("Created workflow definition: {Id} ({Name})", workflow.Id, workflow.Name);
        return workflow;
    }

    public async Task<WorkflowDefinition?> GetWorkflowByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;

        using var connection = _storageContext.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM workflows WHERE id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", id);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return ReadWorkflow(reader);
        }

        return null;
    }

    public async Task<IReadOnlyList<WorkflowDefinition>> GetAllWorkflowsAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<WorkflowDefinition>();

        using var connection = _storageContext.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM workflows ORDER BY category, name ASC;";

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(ReadWorkflow(reader));
        }

        return list;
    }

    public async Task UpdateWorkflowAsync(WorkflowDefinition workflow, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        using var connection = _storageContext.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            UPDATE workflows SET
                name = $name,
                description = $description,
                category = $category,
                trigger_type = $triggerType,
                trigger_config_json = $triggerConfig,
                inputs_json = $inputs,
                actions_json = $actions,
                timeout_seconds = $timeout,
                completion_behavior = $completionBehavior,
                updated_at = $updatedAt,
                is_enabled = $isEnabled
            WHERE id = $id;
        ";

        cmd.Parameters.AddWithValue("$id", workflow.Id);
        cmd.Parameters.AddWithValue("$name", workflow.Name);
        cmd.Parameters.AddWithValue("$description", (object?)workflow.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$category", workflow.Category);
        cmd.Parameters.AddWithValue("$triggerType", (int)workflow.Trigger);
        cmd.Parameters.AddWithValue("$triggerConfig", (object?)workflow.TriggerConfigJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$inputs", JsonSerializer.Serialize(workflow.Inputs));
        cmd.Parameters.AddWithValue("$actions", JsonSerializer.Serialize(workflow.Actions));
        cmd.Parameters.AddWithValue("$timeout", workflow.TimeoutSeconds);
        cmd.Parameters.AddWithValue("$completionBehavior", (int)workflow.CompletionBehavior);
        cmd.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$isEnabled", workflow.IsEnabled ? 1 : 0);

        var affected = await cmd.ExecuteNonQueryAsync(cancellationToken);
        if (affected == 0)
        {
            throw new KeyNotFoundException($"Workflow with ID '{workflow.Id}' not found for update.");
        }
    }

    public async Task DeleteWorkflowAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id)) return;

        using var connection = _storageContext.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM workflows WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
        _logger?.LogInformation("Deleted workflow definition: {Id}", id);
    }

    public async Task RecordRunAsync(WorkflowRunRecord runRecord, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runRecord);

        using var connection = _storageContext.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO workflow_runs (
                run_id, workflow_id, status, current_step,
                started_at, completed_at, duration_ms, sanitized_status_info
            ) VALUES (
                $runId, $workflowId, $status, $currentStep,
                $startedAt, $completedAt, $durationMs, $statusInfo
            )
            ON CONFLICT(run_id) DO UPDATE SET
                status = excluded.status,
                current_step = excluded.current_step,
                completed_at = excluded.completed_at,
                duration_ms = excluded.duration_ms,
                sanitized_status_info = excluded.sanitized_status_info;
        ";

        cmd.Parameters.AddWithValue("$runId", runRecord.RunId);
        cmd.Parameters.AddWithValue("$workflowId", runRecord.WorkflowId);
        cmd.Parameters.AddWithValue("$status", (int)runRecord.Status);
        cmd.Parameters.AddWithValue("$currentStep", runRecord.CurrentStep);
        cmd.Parameters.AddWithValue("$startedAt", runRecord.StartedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$completedAt", runRecord.CompletedAt.HasValue ? runRecord.CompletedAt.Value.ToString("O") : DBNull.Value);
        cmd.Parameters.AddWithValue("$durationMs", (object?)runRecord.DurationMs ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$statusInfo", (object?)runRecord.SanitizedStatusInfo ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
        _logger?.LogDebug("Recorded workflow run record: RunId={RunId}, Status={Status}", runRecord.RunId, runRecord.Status);
    }

    public async Task<IReadOnlyList<WorkflowRunRecord>> GetRunHistoryAsync(string workflowId, int limit = 50, CancellationToken cancellationToken = default)
    {
        var list = new List<WorkflowRunRecord>();

        using var connection = _storageContext.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM workflow_runs WHERE workflow_id = $wfId ORDER BY started_at DESC LIMIT $limit;";
        cmd.Parameters.AddWithValue("$wfId", workflowId);
        cmd.Parameters.AddWithValue("$limit", limit);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(ReadRunRecord(reader));
        }

        return list;
    }

    public async Task<WorkflowRunRecord?> GetRunByIdAsync(string runId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(runId)) return null;

        using var connection = _storageContext.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM workflow_runs WHERE run_id = $runId LIMIT 1;";
        cmd.Parameters.AddWithValue("$runId", runId);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return ReadRunRecord(reader);
        }

        return null;
    }

    private static WorkflowDefinition ReadWorkflow(SqliteDataReader reader)
    {
        var inputsJson = reader.GetString(reader.GetOrdinal("inputs_json"));
        var actionsJson = reader.GetString(reader.GetOrdinal("actions_json"));

        var inputs = JsonSerializer.Deserialize<List<string>>(inputsJson) ?? new List<string>();
        var actions = JsonSerializer.Deserialize<List<WorkflowActionDefinition>>(actionsJson) ?? new List<WorkflowActionDefinition>();

        return new WorkflowDefinition(
            Id: reader.GetString(reader.GetOrdinal("id")),
            Name: reader.GetString(reader.GetOrdinal("name")),
            Description: reader.IsDBNull(reader.GetOrdinal("description")) ? string.Empty : reader.GetString(reader.GetOrdinal("description")),
            Category: reader.GetString(reader.GetOrdinal("category")),
            Trigger: (TriggerType)reader.GetInt32(reader.GetOrdinal("trigger_type")),
            TriggerConfigJson: reader.IsDBNull(reader.GetOrdinal("trigger_config_json")) ? null : reader.GetString(reader.GetOrdinal("trigger_config_json")),
            Inputs: inputs,
            Actions: actions,
            TimeoutSeconds: reader.GetInt32(reader.GetOrdinal("timeout_seconds")),
            CompletionBehavior: (CompletionBehavior)reader.GetInt32(reader.GetOrdinal("completion_behavior")),
            CreatedAt: DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("created_at"))),
            UpdatedAt: DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("updated_at"))),
            IsEnabled: reader.GetInt32(reader.GetOrdinal("is_enabled")) == 1
        );
    }

    private static WorkflowRunRecord ReadRunRecord(SqliteDataReader reader)
    {
        return new WorkflowRunRecord(
            RunId: reader.GetString(reader.GetOrdinal("run_id")),
            WorkflowId: reader.GetString(reader.GetOrdinal("workflow_id")),
            Status: (WorkflowExecutionStatus)reader.GetInt32(reader.GetOrdinal("status")),
            CurrentStep: reader.GetInt32(reader.GetOrdinal("current_step")),
            StartedAt: DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("started_at"))),
            CompletedAt: reader.IsDBNull(reader.GetOrdinal("completed_at")) ? null : DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("completed_at"))),
            DurationMs: reader.IsDBNull(reader.GetOrdinal("duration_ms")) ? null : reader.GetInt64(reader.GetOrdinal("duration_ms")),
            SanitizedStatusInfo: reader.IsDBNull(reader.GetOrdinal("sanitized_status_info")) ? null : reader.GetString(reader.GetOrdinal("sanitized_status_info"))
        );
    }
}
