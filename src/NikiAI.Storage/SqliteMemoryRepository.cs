using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using NikiAI.Core.Memory;

namespace NikiAI.Storage;

/// <summary>
/// SQLite-backed persistent memory repository implementing IMemoryStore.
/// Manages explicit long-term memories, project knowledge, and transient short-term notes.
/// </summary>
public class SqliteMemoryRepository : IMemoryStore
{
    private readonly StorageContext _context;
    private readonly ILogger<SqliteMemoryRepository>? _logger;

    public SqliteMemoryRepository(StorageContext context, ILogger<SqliteMemoryRepository>? logger = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger;
    }

    public async Task SaveMemoryAsync(MemoryItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        cancellationToken.ThrowIfCancellationRequested();

        using var connection = _context.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO memory_items (
                id, category, content, created_at, updated_at, context_explanation,
                project_id, metadata_json, is_pinned, expires_at
            ) VALUES (
                $id, $category, $content, $createdAt, $updatedAt, $contextExplanation,
                $projectId, $metadataJson, $isPinned, $expiresAt
            )
            ON CONFLICT(id) DO UPDATE SET
                category = excluded.category,
                content = excluded.content,
                updated_at = excluded.updated_at,
                context_explanation = excluded.context_explanation,
                project_id = excluded.project_id,
                metadata_json = excluded.metadata_json,
                is_pinned = excluded.is_pinned,
                expires_at = excluded.expires_at;
        ";

        var now = DateTimeOffset.UtcNow;
        cmd.Parameters.AddWithValue("$id", item.Id);
        cmd.Parameters.AddWithValue("$category", item.Category.ToString());
        cmd.Parameters.AddWithValue("$content", item.Content);
        cmd.Parameters.AddWithValue("$createdAt", item.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$updatedAt", (item.UpdatedAt ?? now).ToString("O"));
        cmd.Parameters.AddWithValue("$contextExplanation", (object?)item.ContextExplanation ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$projectId", (object?)item.ProjectId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$metadataJson", (object?)item.MetadataJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$isPinned", item.IsPinned ? 1 : 0);
        cmd.Parameters.AddWithValue("$expiresAt", item.ExpiresAt.HasValue ? item.ExpiresAt.Value.ToString("O") : DBNull.Value);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
        _logger?.LogDebug("Saved memory item {MemoryId} in category {Category}", item.Id, item.Category);
    }

    public async Task<MemoryItem?> GetMemoryByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        cancellationToken.ThrowIfCancellationRequested();

        using var connection = _context.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT id, category, content, created_at, updated_at, context_explanation,
                   project_id, metadata_json, is_pinned, expires_at
            FROM memory_items
            WHERE id = $id;
        ";
        cmd.Parameters.AddWithValue("$id", id);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return MapReaderToMemoryItem(reader);
        }

        return null;
    }

    public async Task<IReadOnlyList<MemoryItem>> GetMemoriesAsync(MemoryCategory category, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var connection = _context.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        var nowStr = DateTimeOffset.UtcNow.ToString("O");
        cmd.CommandText = @"
            SELECT id, category, content, created_at, updated_at, context_explanation,
                   project_id, metadata_json, is_pinned, expires_at
            FROM memory_items
            WHERE category = $category
              AND (expires_at IS NULL OR expires_at > $now)
            ORDER BY is_pinned DESC, created_at DESC;
        ";
        cmd.Parameters.AddWithValue("$category", category.ToString());
        cmd.Parameters.AddWithValue("$now", nowStr);

        var list = new List<MemoryItem>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(MapReaderToMemoryItem(reader));
        }

        return list;
    }

    public async Task<IReadOnlyList<MemoryItem>> SearchMemoriesAsync(string query, MemoryCategory? category = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var connection = _context.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        var nowStr = DateTimeOffset.UtcNow.ToString("O");
        var sql = @"
            SELECT id, category, content, created_at, updated_at, context_explanation,
                   project_id, metadata_json, is_pinned, expires_at
            FROM memory_items
            WHERE (expires_at IS NULL OR expires_at > $now)
        ";

        if (category.HasValue)
        {
            sql += " AND category = $category ";
            cmd.Parameters.AddWithValue("$category", category.Value.ToString());
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            sql += " AND (content LIKE $query OR context_explanation LIKE $query) ";
            cmd.Parameters.AddWithValue("$query", $"%{query.Trim()}%");
        }

        sql += " ORDER BY is_pinned DESC, created_at DESC;";
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$now", nowStr);

        var list = new List<MemoryItem>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(MapReaderToMemoryItem(reader));
        }

        return list;
    }

    public async Task<IReadOnlyList<MemoryItem>> GetProjectMemoriesAsync(string projectId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        cancellationToken.ThrowIfCancellationRequested();

        using var connection = _context.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT id, category, content, created_at, updated_at, context_explanation,
                   project_id, metadata_json, is_pinned, expires_at
            FROM memory_items
            WHERE category = 'Project' AND project_id = $projectId
            ORDER BY is_pinned DESC, created_at DESC;
        ";
        cmd.Parameters.AddWithValue("$projectId", projectId);

        var list = new List<MemoryItem>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(MapReaderToMemoryItem(reader));
        }

        return list;
    }

    public async Task DeleteMemoryAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        cancellationToken.ThrowIfCancellationRequested();

        using var connection = _context.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM memory_items WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
        _logger?.LogDebug("Deleted memory item {MemoryId}", id);
    }

    public async Task ClearCategoryAsync(MemoryCategory category, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var connection = _context.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM memory_items WHERE category = $category;";
        cmd.Parameters.AddWithValue("$category", category.ToString());

        var affected = await cmd.ExecuteNonQueryAsync(cancellationToken);
        _logger?.LogInformation("Cleared category {Category}, removing {Count} items", category, affected);
    }

    private static MemoryItem MapReaderToMemoryItem(IDataRecord reader)
    {
        var id = reader.GetString(0);
        var categoryStr = reader.GetString(1);
        var content = reader.GetString(2);
        var createdAt = DateTimeOffset.Parse(reader.GetString(3));
        var updatedAt = DateTimeOffset.Parse(reader.GetString(4));
        var contextExp = reader.IsDBNull(5) ? null : reader.GetString(5);
        var projectId = reader.IsDBNull(6) ? null : reader.GetString(6);
        var metadataJson = reader.IsDBNull(7) ? null : reader.GetString(7);
        var isPinned = reader.GetInt64(8) != 0;
        DateTimeOffset? expiresAt = reader.IsDBNull(9) ? null : DateTimeOffset.Parse(reader.GetString(9));

        if (!Enum.TryParse<MemoryCategory>(categoryStr, out var category))
        {
            category = MemoryCategory.LongTerm;
        }

        return new MemoryItem(
            Id: id,
            Category: category,
            Content: content,
            CreatedAt: createdAt,
            ContextExplanation: contextExp,
            UpdatedAt: updatedAt,
            ProjectId: projectId,
            MetadataJson: metadataJson,
            IsPinned: isPinned,
            ExpiresAt: expiresAt
        );
    }
}
