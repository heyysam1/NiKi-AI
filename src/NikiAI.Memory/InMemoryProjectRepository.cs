using System.Collections.Concurrent;
using NikiAI.Core.Memory;

namespace NikiAI.Memory;

public class InMemoryProjectRepository : IProjectRepository
{
    private readonly ConcurrentDictionary<string, Project> _projects = new();

    public Task<Project> CreateProjectAsync(Project project, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        _projects[project.Id] = project;
        return Task.FromResult(project);
    }

    public Task<Project?> GetProjectByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        _projects.TryGetValue(id, out var project);
        return Task.FromResult(project);
    }

    public Task<IReadOnlyList<Project>> GetAllProjectsAsync(CancellationToken cancellationToken = default)
    {
        var list = _projects.Values.OrderByDescending(p => p.CreatedAt).ToList();
        return Task.FromResult<IReadOnlyList<Project>>(list);
    }

    public Task<bool> UpdateProjectAsync(Project project, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (!_projects.ContainsKey(project.Id)) return Task.FromResult(false);
        _projects[project.Id] = project;
        return Task.FromResult(true);
    }

    public Task<bool> DeleteProjectAsync(string id, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_projects.TryRemove(id, out _));
    }
}
