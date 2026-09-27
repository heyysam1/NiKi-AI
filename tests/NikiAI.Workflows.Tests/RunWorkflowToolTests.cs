using NikiAI.Core.Tools;
using NikiAI.Core.Workflows;
using NikiAI.Workflows;
using Xunit;

namespace NikiAI.Workflows.Tests;

public class RunWorkflowToolTests
{
    private class FakeWorkflowEngine : IWorkflowEngine
    {
        public event Action<WorkflowRunRecord>? RunStatusChanged;

        public Task<WorkflowRunRecord> ExecuteWorkflowAsync(string workflowId, IReadOnlyDictionary<string, string>? inputs = null, CancellationToken cancellationToken = default)
        {
            var record = new WorkflowRunRecord(
                RunId: "test-run-123",
                WorkflowId: workflowId,
                Status: WorkflowExecutionStatus.Completed,
                CurrentStep: 2,
                StartedAt: DateTimeOffset.UtcNow.AddSeconds(-2),
                CompletedAt: DateTimeOffset.UtcNow,
                DurationMs: 2000,
                SanitizedStatusInfo: "Completed successfully"
            );

            RunStatusChanged?.Invoke(record);
            return Task.FromResult(record);
        }

        public Task<WorkflowRunRecord> ResumeWorkflowAsync(string runId, bool approved, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task CancelWorkflowAsync(string runId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    [Fact]
    public async Task ExecuteAsync_ValidWorkflowCall_ExecutesAndReturnsStructuredResult()
    {
        var engine = new FakeWorkflowEngine();
        var tool = new RunWorkflowTool(engine);

        var call = new ToolCall("call_1", tool.Id, """{"workflow_id": "wf-daily-summary"}""", DateTimeOffset.UtcNow);
        var result = await tool.ExecuteAsync(call);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.OutputJson);
        Assert.Contains("test-run-123", result.OutputJson);
        Assert.Contains("Completed", result.OutputJson);
    }

    [Fact]
    public async Task ExecuteAsync_MissingWorkflowId_ReturnsFailureResult()
    {
        var engine = new FakeWorkflowEngine();
        var tool = new RunWorkflowTool(engine);

        var call = new ToolCall("call_2", tool.Id, """{"inputs": {}}""", DateTimeOffset.UtcNow);
        var result = await tool.ExecuteAsync(call);

        Assert.False(result.IsSuccess);
        Assert.Contains("Missing required parameter 'workflow_id'", result.ErrorMessage);
    }
}
