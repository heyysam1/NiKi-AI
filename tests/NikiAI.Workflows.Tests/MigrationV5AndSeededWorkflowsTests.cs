using System.IO;
using NikiAI.Core.Workflows;
using NikiAI.Storage;
using Xunit;

namespace NikiAI.Workflows.Tests;

public class MigrationV5AndSeededWorkflowsTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly StorageContext _storageContext;
    private readonly SqliteWorkflowRepository _workflowRepo;

    public MigrationV5AndSeededWorkflowsTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"niki_wf_mig_{Guid.NewGuid():N}.db");
        _storageContext = new StorageContext(_testDbPath);
        _storageContext.InitializeAsync().GetAwaiter().GetResult();
        _workflowRepo = new SqliteWorkflowRepository(_storageContext);
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_testDbPath)) File.Delete(_testDbPath);
        }
        catch { }
    }

    [Fact]
    public async Task MigrationV5_SeedsThreeRealWorkflows_WithoutAutoExecuting()
    {
        var workflows = await _workflowRepo.GetAllWorkflowsAsync();
        Assert.True(workflows.Count >= 3);

        var startWork = workflows.FirstOrDefault(w => w.Id == "wf-start-work");
        var research = workflows.FirstOrDefault(w => w.Id == "wf-prepare-research");
        var workday = workflows.FirstOrDefault(w => w.Id == "wf-end-workday");

        Assert.NotNull(startWork);
        Assert.NotNull(research);
        Assert.NotNull(workday);

        // 1. Verify Start Work
        Assert.Equal("Start Work", startWork.Name);
        Assert.Equal(3, startWork.Actions.Count);
        Assert.Equal("open_app", startWork.Actions[0].ToolId);
        Assert.Equal("create_reminder", startWork.Actions[1].ToolId);
        Assert.Equal(ActionType.Notification, startWork.Actions[2].ActionType);

        // 2. Verify Prepare a Research Session
        Assert.Equal("Prepare a Research Session", research.Name);
        Assert.Equal(3, research.Actions.Count);
        Assert.Equal("search_web", research.Actions[0].ToolId);
        Assert.Equal("write_clipboard", research.Actions[1].ToolId);
        Assert.Equal(ActionType.Notification, research.Actions[2].ActionType);

        // 3. Verify End Workday
        Assert.Equal("End Workday", workday.Name);
        Assert.Equal(3, workday.Actions.Count);
        Assert.Equal("recent_apps", workday.Actions[0].ToolId);
        Assert.Equal("create_reminder", workday.Actions[1].ToolId);
        Assert.Equal(ActionType.Notification, workday.Actions[2].ActionType);

        // 4. Verify ZERO auto-executions occurred during migration or startup
        var startHistory = await _workflowRepo.GetRunHistoryAsync(startWork.Id);
        var researchHistory = await _workflowRepo.GetRunHistoryAsync(research.Id);
        var workdayHistory = await _workflowRepo.GetRunHistoryAsync(workday.Id);

        Assert.Empty(startHistory);
        Assert.Empty(researchHistory);
        Assert.Empty(workdayHistory);
    }
}
