using Microsoft.Data.Sqlite;
using NikiAI.Core.Memory;
using NikiAI.Storage;
using Xunit;

namespace NikiAI.Storage.Tests;

public sealed class MemoryRepositoryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly StorageContext _storageContext;
    private readonly SqliteMemoryRepository _memoryRepo;
    private readonly SqliteProjectRepository _projectRepo;
    private readonly SqliteTimelineRepository _timelineRepo;

    public MemoryRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"niki_memory_test_{Guid.NewGuid():N}.db");
        _storageContext = new StorageContext(_dbPath);
        _storageContext.InitializeAsync().GetAwaiter().GetResult();

        _memoryRepo = new SqliteMemoryRepository(_storageContext);
        _projectRepo = new SqliteProjectRepository(_storageContext);
        _timelineRepo = new SqliteTimelineRepository(_storageContext);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
    }

    [Fact]
    public async Task MemoryRepository_SaveAndRetrieve_PersistsCorrectly()
    {
        var now = DateTimeOffset.UtcNow;
        var item = new MemoryItem(
            Id: Guid.NewGuid().ToString("N"),
            Category: MemoryCategory.LongTerm,
            Content: "Earl Grey with honey",
            CreatedAt: now,
            ContextExplanation: "preferred_drink",
            UpdatedAt: now,
            ProjectId: null,
            MetadataJson: null,
            IsPinned: true,
            ExpiresAt: null);

        await _memoryRepo.SaveMemoryAsync(item);

        var retrieved = await _memoryRepo.GetMemoryByIdAsync(item.Id);
        Assert.NotNull(retrieved);
        Assert.Equal("preferred_drink", retrieved.ContextExplanation);
        Assert.Equal("Earl Grey with honey", retrieved.Content);
        Assert.True(retrieved.IsPinned);

        var searchResults = await _memoryRepo.SearchMemoriesAsync("Earl Grey");
        Assert.Single(searchResults);
        Assert.Equal(item.Id, searchResults[0].Id);
    }

    [Fact]
    public async Task MemoryRepository_DeleteAndClear_RemovesData()
    {
        var now = DateTimeOffset.UtcNow;
        var item1 = new MemoryItem(Guid.NewGuid().ToString("N"), MemoryCategory.Project, "note1", now, "k1");
        var item2 = new MemoryItem(Guid.NewGuid().ToString("N"), MemoryCategory.Project, "note2", now, "k2");
        var item3 = new MemoryItem(Guid.NewGuid().ToString("N"), MemoryCategory.LongTerm, "note3", now, "k3");

        await _memoryRepo.SaveMemoryAsync(item1);
        await _memoryRepo.SaveMemoryAsync(item2);
        await _memoryRepo.SaveMemoryAsync(item3);

        await _memoryRepo.DeleteMemoryAsync(item1.Id);

        await _memoryRepo.ClearCategoryAsync(MemoryCategory.Project);

        var projectLeft = await _memoryRepo.GetMemoriesAsync(MemoryCategory.Project);
        var longLeft = await _memoryRepo.GetMemoriesAsync(MemoryCategory.LongTerm);

        Assert.Empty(projectLeft);
        Assert.Single(longLeft);
    }

    [Fact]
    public async Task ProjectRepository_CreateAndQuery_PersistsCorrectly()
    {
        var now = DateTimeOffset.UtcNow;
        var project = new Project(
            Id: Guid.NewGuid().ToString("N"),
            Name: "Project Beta",
            Description: "Test project",
            CreatedAt: now,
            UpdatedAt: now);

        await _projectRepo.CreateProjectAsync(project);

        var fetched = await _projectRepo.GetProjectByIdAsync(project.Id);
        Assert.NotNull(fetched);
        Assert.Equal("Project Beta", fetched.Name);

        var all = await _projectRepo.GetAllProjectsAsync();
        Assert.Contains(all, p => p.Id == project.Id);

        var updated = await _projectRepo.UpdateProjectAsync(project with { Description = "Updated description" });
        Assert.True(updated);

        var deleted = await _projectRepo.DeleteProjectAsync(project.Id);
        Assert.True(deleted);

        var fetchAfterDelete = await _projectRepo.GetProjectByIdAsync(project.Id);
        Assert.Null(fetchAfterDelete);
    }

    [Fact]
    public async Task TimelineRepository_LogAndQuery_PersistsCorrectly()
    {
        var now = DateTimeOffset.UtcNow;
        var evt = new TimelineEvent(
            Id: Guid.NewGuid().ToString("N"),
            EventType: "tool_executed",
            Source: "tool:browser_search",
            Summary: "Executed browser search",
            DetailsJson: null,
            Timestamp: now);

        await _timelineRepo.LogEventAsync(evt);

        var recent = await _timelineRepo.GetRecentEventsAsync(5);
        Assert.Contains(recent, e => e.Id == evt.Id);

        var cleared = await _timelineRepo.ClearAllEventsAsync();
        Assert.True(cleared >= 1);

        var afterClear = await _timelineRepo.GetRecentEventsAsync(5);
        Assert.Empty(afterClear);
    }
}
