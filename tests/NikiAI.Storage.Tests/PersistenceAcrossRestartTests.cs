using NikiAI.Core.Tasks;
using Xunit;

namespace NikiAI.Storage.Tests;

public class PersistenceAcrossRestartTests : IDisposable
{
    private readonly string _testDbDir;
    private readonly string _testDbPath;

    public PersistenceAcrossRestartTests()
    {
        _testDbDir = Path.Combine(Path.GetTempPath(), "NikiAI_RestartTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDbDir);
        _testDbPath = Path.Combine(_testDbDir, "restart_test_niki.db");
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
            // Ignore file lock cleanup in temp directory
        }
    }

    [Fact]
    public async Task TasksAndEvents_ShouldPersistAcrossDatabaseRestartWithIdenticalFidelity()
    {
        var taskId = "persisted_task_123";

        // Phase A: Write task in initial database session
        {
            var initialContext = new StorageContext(_testDbPath);
            await initialContext.InitializeAsync();
            var initialRepo = new SqliteTaskRepository(initialContext);

            var task = new AgentTask(
                title: "Persisted Across Restart Task",
                naturalLanguageRequest: "Original user request",
                structuredGoal: "Restart Hydration",
                priority: AgentTaskPriority.Critical)
            {
                Id = taskId,
                Status = AgentTaskStatus.Pending,
                ProgressPercentage = 75,
                IsArchived = false
            };

            await initialRepo.CreateAsync(task);
            await initialRepo.TransitionStatusAsync(taskId, AgentTaskStatus.Running, "Started in Session 1");
            await initialRepo.TransitionStatusAsync(taskId, AgentTaskStatus.Completed, "Completed in Session 1");
            await initialRepo.ArchiveAsync(taskId, "Archived in Session 1");
        } // All connections from Session 1 disposed

        // Phase B: Re-open database with a completely fresh StorageContext and SqliteTaskRepository
        {
            var freshContext = new StorageContext(_testDbPath);
            // Verify migration engine recognizes already applied V1 schema without error or reset
            await freshContext.InitializeAsync();
            var freshRepo = new SqliteTaskRepository(freshContext);

            var hydrated = await freshRepo.GetByIdAsync(taskId);
            Assert.NotNull(hydrated);
            Assert.Equal(taskId, hydrated.Id);
            Assert.Equal("Persisted Across Restart Task", hydrated.Title);
            Assert.Equal("Original user request", hydrated.NaturalLanguageRequest);
            Assert.Equal("Restart Hydration", hydrated.StructuredGoal);
            Assert.Equal(AgentTaskPriority.Critical, hydrated.Priority);
            Assert.Equal(AgentTaskStatus.Completed, hydrated.Status);
            Assert.Equal(75, hydrated.ProgressPercentage);
            Assert.True(hydrated.IsArchived);
            Assert.NotNull(hydrated.StartedAt);
            Assert.NotNull(hydrated.CompletedAt);

            // Verify complete audit events chain is 100% hydrated
            var hydratedEvents = await freshRepo.GetEventsAsync(taskId);
            Assert.Equal(4, hydratedEvents.Count);
            Assert.Equal(TaskEventType.Created, hydratedEvents[0].EventType);
            Assert.Equal(TaskEventType.StatusChanged, hydratedEvents[1].EventType);
            Assert.Equal(TaskEventType.StatusChanged, hydratedEvents[2].EventType);
            Assert.Equal(TaskEventType.Archived, hydratedEvents[3].EventType);
        }
    }
}
