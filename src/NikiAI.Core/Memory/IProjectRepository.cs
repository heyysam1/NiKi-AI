namespace NikiAI.Core.Memory;

/// <summary>
/// Domain model representing an explicit project boundary with its own memory items and goals.
/// </summary>
public record Project(
    string Id,
    string Name,
    string? Description,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? MetadataJson = null
);

/// <summary>
/// Repository contract for managing projects in the Niki AI project memory system.
/// </summary>
public interface IProjectRepository
{
    Task<Project> CreateProjectAsync(Project project, CancellationToken cancellationToken = default);
    Task<Project?> GetProjectByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Project>> GetAllProjectsAsync(CancellationToken cancellationToken = default);
    Task<bool> UpdateProjectAsync(Project project, CancellationToken cancellationToken = default);
    Task<bool> DeleteProjectAsync(string id, CancellationToken cancellationToken = default);
}
