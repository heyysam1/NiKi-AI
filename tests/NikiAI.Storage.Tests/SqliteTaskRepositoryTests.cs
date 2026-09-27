using NikiAI.Core.Tasks;
using Xunit;

namespace NikiAI.Storage.Tests;

public class SqliteTaskRepositoryTests : IDisposable
{
    private readonly string _testDbDir;
    private readonly string _testDbPath;
    private readonly StorageContext _storageContext;
    private readonly SqliteTaskRepository _repository;

    public SqliteTaskRepositoryTests()
    {
        // Strict isolated temporary database for automated storage testing
        _testDbDir = Path.Combine(Path.GetTempPath(), "NikiAI_StorageTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDbDir);
        _testDbPath = Path.Combine(_testDbDir, "test_niki.db");

        _storageContext = new StorageContext(_testDbPath);
        _storageContext.InitializeAsync().GetAwaiter().GetResult();
        _repository = new SqliteTaskRepository(_storageContext);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDbDir))
            {
                Directory.Delete(_testDbDir, recursive: true);
            }
        }
        catch
        {
            // Ignore file lock cleanup issues in temp directory
        }
    }

    [Fact]
    public async Task CreateAsync_ShouldPersistTaskAndCreateInitialAuditEvent()
    {
        var task = new AgentTask(
            title: "Unit Test Task",
            naturalLanguageRequest: "Please process this request",
            structuredGoal: "Unit Testing",
            priority: AgentTaskPriority.High)
        {
            Status = AgentTaskStatus.Pending,
            ProgressPercentage = null
        };

        var created = await _repository.CreateAsync(task);
        Assert.NotNull(created);

        var fetched = await _repository.GetByIdAsync(task.Id);
        Assert.NotNull(fetched);
        Assert.Equal(task.Title, fetched.Title);
        Assert.Equal(task.NaturalLanguageRequest, fetched.NaturalLanguageRequest);
        Assert.Equal(AgentTaskStatus.Pending, fetched.Status);
        Assert.Null(fetched.ProgressPercentage); // Truthful progress: null

        // Verify initial audit event
        var events = await _repository.GetEventsAsync(task.Id);
        Assert.Single(events);
        Assert.Equal(TaskEventType.Created, events[0].EventType);
        Assert.Contains(task.Title, events[0].Message);
    }

    [Fact]
    public async Task TransitionStatusAsync_ShouldAtomicallyUpdateStatusAndWriteAuditEvent()
    {
        var task = new AgentTask(
            title: "Lifecycle Transition Task",
            naturalLanguageRequest: "Execute pipeline")
        {
            Status = AgentTaskStatus.Pending
        };
        await _repository.CreateAsync(task);

        // Atomic transition: Pending -> Running
        await _repository.TransitionStatusAsync(task.Id, AgentTaskStatus.Running, "Starting task worker");

        var running = await _repository.GetByIdAsync(task.Id);
        Assert.NotNull(running);
        Assert.Equal(AgentTaskStatus.Running, running.Status);
        Assert.NotNull(running.StartedAt);

        // Atomic transition: Running -> Completed
        await _repository.TransitionStatusAsync(task.Id, AgentTaskStatus.Completed, "Completed task worker");

        var completed = await _repository.GetByIdAsync(task.Id);
        Assert.NotNull(completed);
        Assert.Equal(AgentTaskStatus.Completed, completed.Status);
        Assert.NotNull(completed.CompletedAt);

        var events = await _repository.GetEventsAsync(task.Id);
        Assert.Equal(3, events.Count);
        Assert.Equal(TaskEventType.Created, events[0].EventType);
        Assert.Equal(TaskEventType.StatusChanged, events[1].EventType);
        Assert.Equal(TaskEventType.StatusChanged, events[2].EventType);
    }

    [Fact]
    public async Task ArchiveAsync_ShouldSoftDeleteTaskAndPreserveAllAuditEvents()
    {
        var task = new AgentTask(
            title: "Task to Archive",
            naturalLanguageRequest: "Archive test")
        {
            Status = AgentTaskStatus.Pending
        };
        await _repository.CreateAsync(task);

        await _repository.TransitionStatusAsync(task.Id, AgentTaskStatus.Running, "Running before archive");

        // Soft delete
        await _repository.ArchiveAsync(task.Id, "Soft deleting for test verification");

        // Default query excludes archived tasks
        var activeTasks = await _repository.GetAllAsync(includeArchived: false);
        Assert.DoesNotContain(activeTasks, t => t.Id == task.Id);

        // Explicit query includes archived tasks
        var allTasks = await _repository.GetAllAsync(includeArchived: true);
        var archived = allTasks.FirstOrDefault(t => t.Id == task.Id);
        Assert.NotNull(archived);
        Assert.True(archived.IsArchived);

        // Audit integrity: all events are preserved!
        var events = await _repository.GetEventsAsync(task.Id);
        Assert.Equal(3, events.Count); // Created + StatusChanged + Archived
        Assert.Equal(TaskEventType.Archived, events.Last().EventType);
    }

    [Fact]
    public async Task PurgePermanentlyAsync_DestroysTaskOnlyWhenExplicitlyRequested()
    {
        var task = new AgentTask(
            title: "Admin Purge Task",
            naturalLanguageRequest: "Testing permanent purge")
        {
            Status = AgentTaskStatus.Draft
        };
        await _repository.CreateAsync(task);

        // Explicit administrative purge
        await _repository.PurgePermanentlyAsync(task.Id);

        var fetched = await _repository.GetByIdAsync(task.Id);
        Assert.Null(fetched);
    }

    [Fact]
    public async Task TruthfulProgress_ShouldPreserveNullAndExactPercentages()
    {
        var task = new AgentTask(
            title: "Truthful Progress Task",
            naturalLanguageRequest: "Testing progress bounds")
        {
            Status = AgentTaskStatus.Pending,
            ProgressPercentage = null
        };
        await _repository.CreateAsync(task);

        var fetchedNull = await _repository.GetByIdAsync(task.Id);
        Assert.Null(fetchedNull!.ProgressPercentage);

        // Update with truthful measurable value
        fetchedNull.ProgressPercentage = 42;
        await _repository.UpdateAsync(fetchedNull);

        var fetchedMeasured = await _repository.GetByIdAsync(task.Id);
        Assert.Equal(42, fetchedMeasured!.ProgressPercentage);
    }
}
