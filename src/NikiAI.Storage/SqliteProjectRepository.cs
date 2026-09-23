using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using NikiAI.Core.Memory;

namespace NikiAI.Storage;

/// <summary>
/// SQLite-backed persistent repository for Projects in the Niki AI project memory system.
/// </summary>
public class SqliteProjectRepository : IProjectRepository
{
    private readonly StorageContext _context;
    private readonly ILogger<SqliteProjectRepository>? _logger;

    public SqliteProjectRepository(StorageContext context, ILogger<SqliteProjectRepository>? logger = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger;
    }

    public async Task<Project> CreateProjectAsync(Project project, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        cancellationToken.ThrowIfCancellationRequested();

        using var connection = _context.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO projects (id, name, description, created_at, updated_at, metadata_json)
            VALUES ($id, $name, $desc, $createdAt, $updatedAt, $metadata);
        ";

        cmd.Parameters.AddWithValue("$id", project.Id);
        cmd.Parameters.AddWithValue("$name", project.Name);
        cmd.Parameters.AddWithValue("$desc", (object?)project.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$createdAt", project.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$updatedAt", project.UpdatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$metadata", (object?)project.MetadataJson ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
        _logger?.LogInformation("Created project {ProjectId}: {Name}", project.Id, project.Name);
        return project;
    }

    public async Task<Project?> GetProjectByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        cancellationToken.ThrowIfCancellationRequested();

        using var connection = _context.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT id, name, description, created_at, updated_at, metadata_json
            FROM projects
            WHERE id = $id;
        ";
        cmd.Parameters.AddWithValue("$id", id);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return MapReaderToProject(reader);
        }

        return null;
    }

    public async Task<IReadOnlyList<Project>> GetAllProjectsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var connection = _context.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT id, name, description, created_at, updated_at, metadata_json
            FROM projects
            ORDER BY created_at DESC;
        ";

        var list = new List<Project>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(MapReaderToProject(reader));
        }

        return list;
    }

    public async Task<bool> UpdateProjectAsync(Project project, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        cancellationToken.ThrowIfCancellationRequested();

        using var connection = _context.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            UPDATE projects
            SET name = $name,
                description = $desc,
                updated_at = $updatedAt,
                metadata_json = $metadata
            WHERE id = $id;
        ";

        cmd.Parameters.AddWithValue("$id", project.Id);
        cmd.Parameters.AddWithValue("$name", project.Name);
        cmd.Parameters.AddWithValue("$desc", (object?)project.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$metadata", (object?)project.MetadataJson ?? DBNull.Value);

        var rows = await cmd.ExecuteNonQueryAsync(cancellationToken);
        return rows > 0;
    }

    public async Task<bool> DeleteProjectAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        cancellationToken.ThrowIfCancellationRequested();

        using var connection = _context.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM projects WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);

        var rows = await cmd.ExecuteNonQueryAsync(cancellationToken);
        _logger?.LogInformation("Deleted project {ProjectId}", id);
        return rows > 0;
    }

    private static Project MapReaderToProject(IDataRecord reader)
    {
        return new Project(
            Id: reader.GetString(0),
            Name: reader.GetString(1),
            Description: reader.IsDBNull(2) ? null : reader.GetString(2),
            CreatedAt: DateTimeOffset.Parse(reader.GetString(3)),
            UpdatedAt: DateTimeOffset.Parse(reader.GetString(4)),
            MetadataJson: reader.IsDBNull(5) ? null : reader.GetString(5)
        );
    }
}
