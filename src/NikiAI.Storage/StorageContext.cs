using System.Data;
using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using NikiAI.Core.Storage;

namespace NikiAI.Storage;

/// <summary>
/// Storage context managing SQLite database initialization, versioned schema migrations, and connection lifecycle.
/// Resolves absolute database path via Windows LOCALAPPDATA without environment string expansion.
/// </summary>
public class StorageContext : IStorageContext
{
    private readonly ILogger<StorageContext>? _logger;
    private readonly string _connectionString;

    public string DatabasePath { get; }
    public string ConnectionString => _connectionString;

    public StorageContext(string? databasePath = null, ILogger<StorageContext>? logger = null)
    {
        _logger = logger;

        if (!string.IsNullOrWhiteSpace(databasePath))
        {
            DatabasePath = Path.GetFullPath(databasePath);
        }
        else
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appFolder = Path.Combine(localAppData, "NikiAI");
            DatabasePath = Path.Combine(appFolder, "niki.db");
        }

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        };

        _connectionString = builder.ToString();
    }

    public SqliteConnection CreateConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        return connection;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var dir = Path.GetDirectoryName(DatabasePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
            _logger?.LogInformation("Created application storage directory: {Directory}", dir);
        }

        using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        // Apply performance and integrity PRAGMAs
        using (var pragmaCmd = connection.CreateCommand())
        {
            pragmaCmd.CommandText = @"
                PRAGMA journal_mode = WAL;
                PRAGMA foreign_keys = ON;
                PRAGMA synchronous = NORMAL;
            ";
            await pragmaCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // Initialize schema_migrations ledger
        using (var initLedgerCmd = connection.CreateCommand())
        {
            initLedgerCmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS schema_migrations (
                    version INTEGER PRIMARY KEY,
                    applied_at TEXT NOT NULL,
                    description TEXT NOT NULL
                );
            ";
            await initLedgerCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // Get currently applied version
        long currentVersion = 0;
        using (var versionCmd = connection.CreateCommand())
        {
            versionCmd.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_migrations;";
            var result = await versionCmd.ExecuteScalarAsync(cancellationToken);
            if (result != null && result != DBNull.Value)
            {
                currentVersion = Convert.ToInt64(result);
            }
        }

        _logger?.LogDebug("Current database schema version: {Version}", currentVersion);

        // Versioned migrations pipeline
        if (currentVersion < 1)
        {
            await ApplyMigration1_TaskSystemAsync(connection, cancellationToken);
        }
        if (currentVersion < 2)
        {
            await ApplyMigration2_PermissionRulesAsync(connection, cancellationToken);
        }
        if (currentVersion < 3)
        {
            await ApplyMigration3_SchedulerAndNotificationsAsync(connection, cancellationToken);
        }
        if (currentVersion < 4)
        {
            await ApplyMigration4_MemoryAndTimelineAsync(connection, cancellationToken);
        }
        if (currentVersion < 5)
        {
            await ApplyMigration5_WorkflowsAsync(connection, cancellationToken);
        }
    }

    private async Task ApplyMigration1_TaskSystemAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        _logger?.LogInformation("Applying database migration V1: Initial Local Task System schema...");

        using var transaction = connection.BeginTransaction();
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = @"
                -- Tasks Table
                CREATE TABLE IF NOT EXISTS tasks (
                    id TEXT PRIMARY KEY,
                    title TEXT NOT NULL,
                    natural_language_request TEXT,
                    structured_goal TEXT,
                    priority INTEGER NOT NULL,
                    status INTEGER NOT NULL,
                    progress_percentage INTEGER,
                    created_at TEXT NOT NULL,
                    started_at TEXT,
                    due_at TEXT,
                    completed_at TEXT,
                    actions_json TEXT,
                    result_summary TEXT,
                    error_information TEXT,
                    artifact_links_json TEXT,
                    is_archived INTEGER NOT NULL DEFAULT 0
                );

                -- Task Audit Events Table (Preserved on soft-delete / archival)
                CREATE TABLE IF NOT EXISTS task_events (
                    id TEXT PRIMARY KEY,
                    task_id TEXT NOT NULL,
                    timestamp TEXT NOT NULL,
                    event_type INTEGER NOT NULL,
                    message TEXT NOT NULL,
                    details_json TEXT
                );

                -- Query Optimization Indexes
                CREATE INDEX IF NOT EXISTS idx_tasks_status ON tasks(status);
                CREATE INDEX IF NOT EXISTS idx_tasks_created_at ON tasks(created_at);
                CREATE INDEX IF NOT EXISTS idx_tasks_is_archived ON tasks(is_archived);
                CREATE INDEX IF NOT EXISTS idx_task_events_task_id ON task_events(task_id);
                CREATE INDEX IF NOT EXISTS idx_task_events_timestamp ON task_events(timestamp);

                -- Record Migration Ledger
                INSERT INTO schema_migrations (version, applied_at, description)
                VALUES (1, $appliedAt, 'V1_TaskSystem');
            ";
            cmd.Parameters.AddWithValue("$appliedAt", DateTimeOffset.UtcNow.ToString("O"));

            await cmd.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            _logger?.LogInformation("Migration V1 applied successfully.");
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger?.LogCritical(ex, "Failed to apply migration V1.");
            throw;
        }
    }

    private async Task ApplyMigration2_PermissionRulesAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        _logger?.LogInformation("Applying database migration V2: Permission rules schema...");

        using var transaction = connection.BeginTransaction();
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = @"
                -- Permission Rules Table
                CREATE TABLE IF NOT EXISTS permission_rules (
                    scope_key TEXT PRIMARY KEY,
                    tool_id TEXT NOT NULL,
                    decision INTEGER NOT NULL,
                    risk_level INTEGER NOT NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );

                CREATE INDEX IF NOT EXISTS idx_permission_rules_tool_id ON permission_rules(tool_id);

                -- Record Migration Ledger
                INSERT INTO schema_migrations (version, applied_at, description)
                VALUES (2, $appliedAt, 'V2_PermissionRules');
            ";
            cmd.Parameters.AddWithValue("$appliedAt", DateTimeOffset.UtcNow.ToString("O"));

            await cmd.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            _logger?.LogInformation("Migration V2 applied successfully.");
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger?.LogCritical(ex, "Failed to apply migration V2.");
            throw;
        }
    }

    private async Task ApplyMigration3_SchedulerAndNotificationsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        _logger?.LogInformation("Applying database migration V3: Scheduler and Notifications schema...");

        using var transaction = connection.BeginTransaction();
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = @"
                -- Scheduled Items Table
                CREATE TABLE IF NOT EXISTS scheduled_items (
                    id TEXT PRIMARY KEY,
                    title TEXT NOT NULL,
                    description TEXT,
                    scheduled_time TEXT NOT NULL,
                    is_recurring INTEGER NOT NULL DEFAULT 0,
                    recurrence_interval_ticks INTEGER,
                    associated_task_id TEXT,
                    item_type INTEGER NOT NULL DEFAULT 0,
                    status INTEGER NOT NULL DEFAULT 0,
                    payload_json TEXT,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );

                CREATE INDEX IF NOT EXISTS idx_scheduled_items_status ON scheduled_items(status);
                CREATE INDEX IF NOT EXISTS idx_scheduled_items_scheduled_time ON scheduled_items(scheduled_time);
                CREATE INDEX IF NOT EXISTS idx_scheduled_items_associated_task_id ON scheduled_items(associated_task_id);

                -- Notifications History Table
                CREATE TABLE IF NOT EXISTS notifications (
                    id TEXT PRIMARY KEY,
                    title TEXT NOT NULL,
                    summary_message TEXT NOT NULL,
                    notification_type INTEGER NOT NULL DEFAULT 0,
                    completion_state INTEGER NOT NULL DEFAULT 0,
                    one_sentence_summary TEXT,
                    associated_task_id TEXT,
                    show_popup INTEGER NOT NULL DEFAULT 1,
                    key_outputs_json TEXT,
                    artifact_links_json TEXT,
                    character_avatar_path TEXT,
                    created_at TEXT NOT NULL,
                    is_read INTEGER NOT NULL DEFAULT 0,
                    is_dismissed INTEGER NOT NULL DEFAULT 0
                );

                CREATE INDEX IF NOT EXISTS idx_notifications_created_at ON notifications(created_at);
                CREATE INDEX IF NOT EXISTS idx_notifications_associated_task_id ON notifications(associated_task_id);

                -- Record Migration Ledger
                INSERT INTO schema_migrations (version, applied_at, description)
                VALUES (3, $appliedAt, 'V3_SchedulerAndNotifications');
            ";
            cmd.Parameters.AddWithValue("$appliedAt", DateTimeOffset.UtcNow.ToString("O"));

            await cmd.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            _logger?.LogInformation("Migration V3 applied successfully.");
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger?.LogCritical(ex, "Failed to apply migration V3.");
            throw;
        }
    }

    private async Task ApplyMigration4_MemoryAndTimelineAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        _logger?.LogInformation("Applying database migration V4: Memory and Activity Timeline schema...");

        using var transaction = connection.BeginTransaction();
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = @"
                -- Memory Items Table
                CREATE TABLE IF NOT EXISTS memory_items (
                    id TEXT PRIMARY KEY,
                    category TEXT NOT NULL,
                    content TEXT NOT NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL,
                    context_explanation TEXT,
                    project_id TEXT,
                    metadata_json TEXT,
                    is_pinned INTEGER NOT NULL DEFAULT 0,
                    expires_at TEXT
                );

                CREATE INDEX IF NOT EXISTS idx_memory_items_category ON memory_items(category);
                CREATE INDEX IF NOT EXISTS idx_memory_items_project_id ON memory_items(project_id);
                CREATE INDEX IF NOT EXISTS idx_memory_items_created_at ON memory_items(created_at);

                -- Projects Table
                CREATE TABLE IF NOT EXISTS projects (
                    id TEXT PRIMARY KEY,
                    name TEXT NOT NULL,
                    description TEXT,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL,
                    metadata_json TEXT
                );

                CREATE INDEX IF NOT EXISTS idx_projects_created_at ON projects(created_at);

                -- Timeline Events Table (Operational Milestone Log, strictly separated from memory)
                CREATE TABLE IF NOT EXISTS timeline_events (
                    id TEXT PRIMARY KEY,
                    event_type TEXT NOT NULL,
                    source TEXT NOT NULL,
                    summary TEXT NOT NULL,
                    details_json TEXT,
                    timestamp TEXT NOT NULL,
                    related_id TEXT
                );

                CREATE INDEX IF NOT EXISTS idx_timeline_events_timestamp ON timeline_events(timestamp);
                CREATE INDEX IF NOT EXISTS idx_timeline_events_related_id ON timeline_events(related_id);

                -- Record Migration Ledger
                INSERT INTO schema_migrations (version, applied_at, description)
                VALUES (4, $appliedAt, 'V4_MemoryAndTimeline');
            ";
            cmd.Parameters.AddWithValue("$appliedAt", DateTimeOffset.UtcNow.ToString("O"));

            await cmd.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            _logger?.LogInformation("Migration V4 applied successfully.");
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger?.LogCritical(ex, "Failed to apply migration V4.");
            throw;
        }
    }

    private async Task ApplyMigration5_WorkflowsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        _logger?.LogInformation("Applying database migration V5: Workflows and Task Lifecycle Signals schema...");

        using var transaction = connection.BeginTransaction();
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = @"
                -- Workflows Table (Definitions only, strictly no retry policy columns)
                CREATE TABLE IF NOT EXISTS workflows (
                    id TEXT PRIMARY KEY,
                    name TEXT NOT NULL,
                    description TEXT,
                    category TEXT NOT NULL,
                    trigger_type INTEGER NOT NULL,
                    trigger_config_json TEXT,
                    inputs_json TEXT,
                    actions_json TEXT NOT NULL,
                    timeout_seconds INTEGER NOT NULL,
                    completion_behavior INTEGER NOT NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL,
                    is_enabled INTEGER NOT NULL DEFAULT 1
                );

                CREATE INDEX IF NOT EXISTS idx_workflows_category ON workflows(category);
                CREATE INDEX IF NOT EXISTS idx_workflows_trigger_type ON workflows(trigger_type);

                -- Workflow Runs Table (Operational metadata only, strictly no arguments, results, inputs, outputs, prompts, clipboard contents, or secrets)
                CREATE TABLE IF NOT EXISTS workflow_runs (
                    run_id TEXT PRIMARY KEY,
                    workflow_id TEXT NOT NULL,
                    status INTEGER NOT NULL,
                    current_step INTEGER NOT NULL,
                    started_at TEXT NOT NULL,
                    completed_at TEXT,
                    duration_ms INTEGER,
                    sanitized_status_info TEXT,
                    FOREIGN KEY(workflow_id) REFERENCES workflows(id) ON DELETE CASCADE
                );

                CREATE INDEX IF NOT EXISTS idx_workflow_runs_workflow_id ON workflow_runs(workflow_id);
                CREATE INDEX IF NOT EXISTS idx_workflow_runs_started_at ON workflow_runs(started_at);

                -- Record Migration Ledger
                INSERT INTO schema_migrations (version, applied_at, description)
                VALUES (5, $appliedAt, 'V5_Workflows');
            ";
            cmd.Parameters.AddWithValue("$appliedAt", DateTimeOffset.UtcNow.ToString("O"));

            await cmd.ExecuteNonQueryAsync(cancellationToken);

            // Seed the 3 real workflows (without executing them)
            await SeedDefaultWorkflowsAsync(connection, transaction, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            _logger?.LogInformation("Migration V5 applied successfully.");
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger?.LogCritical(ex, "Failed to apply migration V5.");
            throw;
        }
    }

    private static async Task SeedDefaultWorkflowsAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow.ToString("O");

        // 1. Start Work
        var startWorkActions = @"[
            {""Id"":""act-start-1"",""Name"":""Open Notepad"",""ActionType"":0,""ToolId"":""open_app"",""ArgumentsJson"":""{\""app_name\"":\""notepad\""}"",""RequiresApproval"":false,""Condition"":null,""TimeoutSeconds"":10},
            {""Id"":""act-start-2"",""Name"":""Create Focus Reminder"",""ActionType"":0,""ToolId"":""create_reminder"",""ArgumentsJson"":""{\""message\"":\""Morning focus check-in: review primary tasks\"",\""delay_seconds\"":3600,\""title\"":\""Focus Check-in\""}"",""RequiresApproval"":false,""Condition"":null,""TimeoutSeconds"":5},
            {""Id"":""act-start-3"",""Name"":""Setup Notification"",""ActionType"":3,""ToolId"":null,""ArgumentsJson"":""{\""title\"":\""Start Work\"",\""message\"":\""Work setup complete.\""}"",""RequiresApproval"":false,""Condition"":null,""TimeoutSeconds"":5}
        ]";

        // 2. Prepare a Research Session
        var researchActions = @"[
            {""Id"":""act-res-1"",""Name"":""Search Web"",""ActionType"":0,""ToolId"":""search_web"",""ArgumentsJson"":""{\""query\"":\""AI agent architectures and workflows\"",\""max_results\"":5}"",""RequiresApproval"":false,""Condition"":null,""TimeoutSeconds"":10},
            {""Id"":""act-res-2"",""Name"":""Copy Research Topic"",""ActionType"":0,""ToolId"":""write_clipboard"",""ArgumentsJson"":""{\""text\"":\""Research topic: AI agent architectures and workflows\""}"",""RequiresApproval"":false,""Condition"":null,""TimeoutSeconds"":5},
            {""Id"":""act-res-3"",""Name"":""Research Notification"",""ActionType"":3,""ToolId"":null,""ArgumentsJson"":""{\""title\"":\""Research Session\"",\""message\"":\""Research preparation ready.\""}"",""RequiresApproval"":false,""Condition"":null,""TimeoutSeconds"":5}
        ]";

        // 3. End Workday
        var endWorkdayActions = @"[
            {""Id"":""act-end-1"",""Name"":""Audit Recent Apps"",""ActionType"":0,""ToolId"":""recent_apps"",""ArgumentsJson"":""{\""limit\"":5}"",""RequiresApproval"":false,""Condition"":null,""TimeoutSeconds"":10},
            {""Id"":""act-end-2"",""Name"":""Schedule Next Day Reminder"",""ActionType"":0,""ToolId"":""create_reminder"",""ArgumentsJson"":""{\""message\"":\""Tomorrow workday priorities planning and recap\"",\""delay_seconds\"":28800,\""title\"":\""Workday Recap\""}"",""RequiresApproval"":false,""Condition"":null,""TimeoutSeconds"":5},
            {""Id"":""act-end-3"",""Name"":""Wrap-up Notification"",""ActionType"":3,""ToolId"":null,""ArgumentsJson"":""{\""title\"":\""End Workday\"",\""message\"":\""Workday wrap-up complete.\""}"",""RequiresApproval"":false,""Condition"":null,""TimeoutSeconds"":5}
        ]";

        var seedQueries = new (string id, string name, string desc, string cat, int trigger, string inputs, string actions)[]
        {
            ("wf-start-work", "Start Work", "Prepares workspace for daily focus: opens notepad, sets morning focus check-in reminder, and notifies completion.", "Work", 0, "[]", startWorkActions),
            ("wf-prepare-research", "Prepare a Research Session", "Prepares for research: queries references, writes research topic to clipboard, and sends confirmation notification.", "Research", 0, "[\"topic\"]", researchActions),
            ("wf-end-workday", "End Workday", "Wraps up the workday: audits active desktop applications, sets tomorrow morning planning reminder, and notifies completion.", "Work", 0, "[]", endWorkdayActions)
        };

        foreach (var item in seedQueries)
        {
            using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = @"
                INSERT OR IGNORE INTO workflows (
                    id, name, description, category, trigger_type,
                    trigger_config_json, inputs_json, actions_json,
                    timeout_seconds, completion_behavior, created_at, updated_at, is_enabled
                ) VALUES (
                    $id, $name, $desc, $cat, $trigger,
                    NULL, $inputs, $actions,
                    60, 0, $createdAt, $updatedAt, 1
                );
            ";
            cmd.Parameters.AddWithValue("$id", item.id);
            cmd.Parameters.AddWithValue("$name", item.name);
            cmd.Parameters.AddWithValue("$desc", item.desc);
            cmd.Parameters.AddWithValue("$cat", item.cat);
            cmd.Parameters.AddWithValue("$trigger", item.trigger);
            cmd.Parameters.AddWithValue("$inputs", item.inputs);
            cmd.Parameters.AddWithValue("$actions", item.actions);
            cmd.Parameters.AddWithValue("$createdAt", now);
            cmd.Parameters.AddWithValue("$updatedAt", now);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}

