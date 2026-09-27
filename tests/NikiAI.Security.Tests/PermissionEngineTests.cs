using NikiAI.Core.Security;
using NikiAI.Core.Tools;
using NikiAI.Security;

namespace NikiAI.Security.Tests;

public class PermissionEngineTests
{
    private class DummyTool : ITool
    {
        public string Id { get; init; } = "dummy.tool";
        public string Name { get; init; } = "Dummy Tool";
        public string Description { get; init; } = "Dummy Description";
        public ToolRiskLevel RiskLevel { get; init; }
        public string InputSchemaJson { get; init; } = "{}";
        public TimeSpan DefaultTimeout { get; init; } = TimeSpan.FromSeconds(5);

        public Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ToolResult.Success(call.CallId, Id, "{}", TimeSpan.Zero));
        }
    }

    [Fact]
    public async Task Evaluate_Level0Informational_IsAutoAllowed()
    {
        var engine = new PermissionEngine();
        var tool = new DummyTool { Id = "tool.info", RiskLevel = ToolRiskLevel.Informational };
        var call = new ToolCall("c1", "tool.info", "{}", DateTimeOffset.UtcNow);

        var result = await engine.EvaluateToolExecutionAsync("task1", tool, call);

        Assert.True(result.IsAllowed);
        Assert.False(result.RequiresUserPrompt);
        Assert.Null(result.PromptRequest);
    }

    [Fact]
    public async Task Evaluate_Level1LowRisk_IsAutoAllowedByDefault()
    {
        var engine = new PermissionEngine();
        var tool = new DummyTool { Id = "tool.lowrisk", RiskLevel = ToolRiskLevel.LowRiskReversible };
        var call = new ToolCall("c2", "tool.lowrisk", "{}", DateTimeOffset.UtcNow);

        var result = await engine.EvaluateToolExecutionAsync("task1", tool, call);

        Assert.True(result.IsAllowed);
        Assert.False(result.RequiresUserPrompt);
    }

    [Fact]
    public async Task Evaluate_Level1LowRisk_IsDeniedIfExplicitlyBlocked()
    {
        var engine = new PermissionEngine();
        var tool = new DummyTool { Id = "tool.lowrisk.blocked", RiskLevel = ToolRiskLevel.LowRiskReversible };
        var call = new ToolCall("c3", tool.Id, "{}", DateTimeOffset.UtcNow);

        engine.RecordDecision(tool.Id, ApprovalDecision.Deny);
        var result = await engine.EvaluateToolExecutionAsync("task1", tool, call);

        Assert.False(result.IsAllowed);
        Assert.False(result.RequiresUserPrompt);
    }

    [Fact]
    public async Task Evaluate_Level2Sensitive_RequiresPromptByDefault()
    {
        var engine = new PermissionEngine();
        var tool = new DummyTool { Id = "tool.sensitive", RiskLevel = ToolRiskLevel.Sensitive };
        var call = new ToolCall("c4", tool.Id, "{}", DateTimeOffset.UtcNow);

        var result = await engine.EvaluateToolExecutionAsync("task1", tool, call);

        Assert.False(result.IsAllowed);
        Assert.True(result.RequiresUserPrompt);
        Assert.NotNull(result.PromptRequest);
        Assert.Equal(tool.Id, result.PromptRequest.ToolId);
        Assert.Equal(ToolRiskLevel.Sensitive, result.PromptRequest.RiskLevel);
    }

    [Fact]
    public async Task Evaluate_Level2Sensitive_AllowedIfAlwaysAllowRecorded()
    {
        var engine = new PermissionEngine();
        var tool = new DummyTool { Id = "tool.sensitive.allowed", RiskLevel = ToolRiskLevel.Sensitive };
        var call = new ToolCall("c5", tool.Id, "{}", DateTimeOffset.UtcNow);

        engine.RecordDecision(tool.Id, ApprovalDecision.AlwaysAllow);
        var result = await engine.EvaluateToolExecutionAsync("task1", tool, call);

        Assert.True(result.IsAllowed);
        Assert.False(result.RequiresUserPrompt);
    }

    [Fact]
    public async Task Evaluate_Level3HighRisk_AlwaysPrompts_NeverAutoAllowed()
    {
        var engine = new PermissionEngine();
        var tool = new DummyTool { Id = "tool.highrisk", RiskLevel = ToolRiskLevel.HighRisk };
        var call = new ToolCall("c6", tool.Id, "{}", DateTimeOffset.UtcNow);

        // Even if some decision was recorded for another tool, HighRisk must always prompt
        var result = await engine.EvaluateToolExecutionAsync("task1", tool, call);

        Assert.False(result.IsAllowed);
        Assert.True(result.RequiresUserPrompt);
        Assert.NotNull(result.PromptRequest);
        Assert.Equal(ToolRiskLevel.HighRisk, result.PromptRequest.RiskLevel);
    }
}
