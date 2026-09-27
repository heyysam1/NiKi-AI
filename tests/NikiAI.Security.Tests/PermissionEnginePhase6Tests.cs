using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NikiAI.Core.Security;
using NikiAI.Core.Tools;
using NikiAI.Security;
using Xunit;

namespace NikiAI.Security.Tests;

public class PermissionEnginePhase6Tests
{
    private class TestTool : ITool
    {
        public string Id { get; init; } = "test_tool";
        public string Name { get; init; } = "Test Tool";
        public string Description { get; init; } = "Test Tool Description";
        public ToolRiskLevel RiskLevel { get; init; } = ToolRiskLevel.Sensitive;
        public string InputSchemaJson { get; init; } = "{}";
        public TimeSpan DefaultTimeout { get; init; } = TimeSpan.FromSeconds(5);

        public Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ToolResult.Success(call.CallId, Id, "{}", TimeSpan.Zero));
        }
    }

    private class MockPromptHandler : IApprovalPromptHandler
    {
        public Func<ApprovalRequest, Task<ApprovalDecisionResult>>? HandlerFunc { get; set; }
        public int PromptCount { get; private set; }

        public Task<ApprovalDecisionResult> RequestApprovalAsync(ApprovalRequest request, CancellationToken cancellationToken = default)
        {
            PromptCount++;
            if (HandlerFunc != null)
            {
                return HandlerFunc(request);
            }
            return Task.FromResult(ApprovalDecisionResult.Denied("Default mock deny"));
        }
    }

    private class MockRuleRepository : IPermissionRuleRepository
    {
        public Dictionary<string, ApprovalDecision> StoredRules { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool ThrowOnLoad { get; set; }

        public Task<IReadOnlyDictionary<string, ApprovalDecision>> LoadRulesAsync(CancellationToken cancellationToken = default)
        {
            if (ThrowOnLoad)
            {
                throw new InvalidOperationException("Simulated SQLite storage corruption or read failure.");
            }
            return Task.FromResult<IReadOnlyDictionary<string, ApprovalDecision>>(StoredRules);
        }

        public Task SaveRuleAsync(string scopeKey, string toolId, ApprovalDecision decision, ToolRiskLevel riskLevel, CancellationToken cancellationToken = default)
        {
            StoredRules[scopeKey] = decision;
            return Task.CompletedTask;
        }

        public Task<bool> DeleteRuleAsync(string scopeKey, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(StoredRules.Remove(scopeKey));
        }

        public Task ClearAllRulesAsync(CancellationToken cancellationToken = default)
        {
            StoredRules.Clear();
            return Task.CompletedTask;
        }
    }

    // 1. Mandatory Correction 1: No crash/exception for Level 3 High-Risk AlwaysAllow attempt
    [Fact]
    public async Task Level3HighRisk_AlwaysAllowAttempt_FailsClosedSafelyWithoutCrash()
    {
        var repo = new MockRuleRepository();
        var audit = new PermissionAuditLogger();
        var engine = new PermissionEngine(repo, audit);
        var promptHandler = new MockPromptHandler
        {
            HandlerFunc = req => Task.FromResult(ApprovalDecisionResult.Approved(ApprovalDecision.AlwaysAllow, "Attempting high risk always allow"))
        };
        engine.SetPromptHandler(promptHandler);

        var highRiskTool = new TestTool { Id = "high_risk_tool", RiskLevel = ToolRiskLevel.HighRisk };
        var call = new ToolCall("c_high", "high_risk_tool", """{ "action": "delete_all" }""", DateTimeOffset.UtcNow);

        // Evaluation prompts because HighRisk cannot be auto-allowed
        var eval = await engine.EvaluateToolExecutionAsync("task_1", highRiskTool, call);
        Assert.True(eval.RequiresUserPrompt);
        Assert.NotNull(eval.PromptRequest);

        // Request approval: prompt handler returned AlwaysAllow. Engine must NOT throw InvalidOperationException.
        var decision = await engine.RequestApprovalAsync(eval.PromptRequest);

        // Fail closed safely!
        Assert.Equal(ApprovalOutcome.Denied, decision.Outcome);
        Assert.Contains("Policy Violation", decision.Reason);

        // Must NOT save a persistent rule for high-risk AlwaysAllow attempt
        Assert.Empty(repo.StoredRules);

        // Verify audit logged the policy violation
        var recentAudit = audit.GetRecentAuditRecords(5);
        Assert.Contains(recentAudit, r => r.Outcome == ApprovalOutcome.Denied && r.SanitizedDetails!.Contains("Policy Violation"));
    }

    // 2. Mandatory Correction 2: Narrow permission scoping (least privilege)
    [Fact]
    public async Task SensitiveTool_NarrowScoping_DoesNotAuthorizeDifferentActionOrResource()
    {
        var repo = new MockRuleRepository();
        var audit = new PermissionAuditLogger();
        var engine = new PermissionEngine(repo, audit);

        var tool = new TestTool { Id = "open_app", RiskLevel = ToolRiskLevel.Sensitive };

        // Save rule specifically for calculator: open_app:launch:calculator
        var calcScope = PermissionScopeKey.Build("open_app", "launch", "calculator");
        await repo.SaveRuleAsync(calcScope, "open_app", ApprovalDecision.AlwaysAllow, ToolRiskLevel.Sensitive);
        await engine.ReloadRulesAsync();

        // Call 1: open_app with calculator -> should be auto-allowed by narrow rule
        var calcCall = new ToolCall("c_calc", "open_app", """{ "app_name": "calculator" }""", DateTimeOffset.UtcNow);
        var calcEval = await engine.EvaluateToolExecutionAsync("task_1", tool, calcCall);
        Assert.True(calcEval.IsAllowed);
        Assert.False(calcEval.RequiresUserPrompt);

        // Call 2: open_app with notepad -> must NOT be authorized by the calculator rule!
        var notepadCall = new ToolCall("c_np", "open_app", """{ "app_name": "notepad" }""", DateTimeOffset.UtcNow);
        var notepadEval = await engine.EvaluateToolExecutionAsync("task_1", tool, notepadCall);
        Assert.False(notepadEval.IsAllowed);
        Assert.True(notepadEval.RequiresUserPrompt);
    }

    // 3. Mandatory Correction 3: Level 1 auto-allowed without redundant prompt
    [Fact]
    public async Task Level1LowRisk_AutoAllowedWithoutPrompt_UnlessExplicitlyDenied()
    {
        var repo = new MockRuleRepository();
        var audit = new PermissionAuditLogger();
        var engine = new PermissionEngine(repo, audit);
        var promptHandler = new MockPromptHandler();
        engine.SetPromptHandler(promptHandler);

        var lowRiskTool = new TestTool { Id = "create_reminder", RiskLevel = ToolRiskLevel.LowRiskReversible };
        var call = new ToolCall("c_rem", "create_reminder", """{ "message": "drink water" }""", DateTimeOffset.UtcNow);

        var eval = await engine.EvaluateToolExecutionAsync("task_1", lowRiskTool, call);
        Assert.True(eval.IsAllowed);
        Assert.False(eval.RequiresUserPrompt);
        Assert.Equal(0, promptHandler.PromptCount);

        // If explicitly denied in rule store:
        var deniedScope = PermissionScopeKey.Build("create_reminder");
        await repo.SaveRuleAsync(deniedScope, "create_reminder", ApprovalDecision.Deny, ToolRiskLevel.LowRiskReversible);
        await engine.ReloadRulesAsync();

        var deniedEval = await engine.EvaluateToolExecutionAsync("task_1", lowRiskTool, call);
        Assert.False(deniedEval.IsAllowed);
        Assert.False(deniedEval.RequiresUserPrompt);
    }

    // 4. Mandatory Correction 4: AllowOnce is strictly bound to current invocation only
    [Fact]
    public async Task AllowOnce_StrictlyBoundToCurrentInvocation_NeverPersistedForSubsequentCalls()
    {
        var repo = new MockRuleRepository();
        var audit = new PermissionAuditLogger();
        var engine = new PermissionEngine(repo, audit);
        var promptHandler = new MockPromptHandler
        {
            HandlerFunc = req => Task.FromResult(ApprovalDecisionResult.Approved(ApprovalDecision.AllowOnce, "One time approval"))
        };
        engine.SetPromptHandler(promptHandler);

        var tool = new TestTool { Id = "write_clipboard", RiskLevel = ToolRiskLevel.Sensitive };
        var call1 = new ToolCall("c_clip_1", "write_clipboard", """{ "text": "secret1" }""", DateTimeOffset.UtcNow);

        // Invocation 1
        var eval1 = await engine.EvaluateToolExecutionAsync("task_1", tool, call1);
        Assert.True(eval1.RequiresUserPrompt);
        var decision1 = await engine.RequestApprovalAsync(eval1.PromptRequest!);
        Assert.Equal(ApprovalOutcome.Approved, decision1.Outcome);
        Assert.Equal(ApprovalDecision.AllowOnce, decision1.Decision);

        // Ensure NOT stored in persistent repository
        Assert.Empty(repo.StoredRules);

        // Invocation 2: Exactly identical tool call must still prompt!
        var call2 = new ToolCall("c_clip_2", "write_clipboard", """{ "text": "secret1" }""", DateTimeOffset.UtcNow);
        var eval2 = await engine.EvaluateToolExecutionAsync("task_1", tool, call2);
        Assert.False(eval2.IsAllowed);
        Assert.True(eval2.RequiresUserPrompt);
    }

    // 5. Mandatory Correction 5: Race-safe approval completion
    [Fact]
    public async Task ApprovalCompletion_RaceSafe_ProducesSingleTerminalOutcome()
    {
        var engine = new PermissionEngine();
        var promptHandler = new MockPromptHandler
        {
            // Simulate prompt taking 100ms before returning Approved
            HandlerFunc = async req =>
            {
                await Task.Delay(100);
                return ApprovalDecisionResult.Approved(ApprovalDecision.AllowOnce);
            }
        };
        engine.SetPromptHandler(promptHandler);

        var request = new ApprovalRequest(
            RequestId: "req_race_1",
            TaskId: "task_race",
            ToolId: "sensitive_tool",
            ScopeKey: "sensitive_tool:*:*",
            RiskLevel: ToolRiskLevel.Sensitive,
            ActionDescription: "Sensitive operation",
            AffectedResource: "System",
            IsReversible: false,
            ExternalDataDisclosureExplanation: null,
            SanitizedArguments: "{}",
            CreatedAt: DateTimeOffset.UtcNow,
            ExpiresAt: DateTimeOffset.UtcNow.AddMilliseconds(50), // Expires sooner than prompt responds!
            Timeout: TimeSpan.FromMilliseconds(50)
        );

        // Race between prompt response (100ms) and expiration (50ms)
        var result = await engine.RequestApprovalAsync(request);

        // Must be TimedOut because expiration arrived first, and outcome must be unique
        Assert.Equal(ApprovalOutcome.TimedOut, result.Outcome);
    }

    // 6. Mandatory Correction 7: Sensitive-data and secret redaction across audit fields
    [Fact]
    public async Task AuditFields_AllContainSecretRedaction_NoRawTokensOrCredentialsLeaked()
    {
        var audit = new PermissionAuditLogger();
        var secretToken = "ghp_1234567890abcdef1234567890abcdef";
        var rawDetails = $"Authorization header Bearer {secretToken} for user account";

        var record = new PermissionAuditRecord(
            RequestId: "req_audit_sec",
            TaskId: "task_sec",
            ToolId: "http_tool",
            ScopeKey: "http_tool:fetch:*",
            RiskLevel: ToolRiskLevel.Sensitive,
            Outcome: ApprovalOutcome.Approved,
            Decision: ApprovalDecision.AllowOnce,
            Actor: "User",
            Timestamp: DateTimeOffset.UtcNow,
            Duration: TimeSpan.FromMilliseconds(12),
            SanitizedDetails: rawDetails
        );

        await audit.LogPermissionEventAsync(record);

        var recent = audit.GetRecentAuditRecords(1);
        Assert.Single(recent);
        var logged = recent[0];

        Assert.DoesNotContain(secretToken, logged.SanitizedDetails);
        Assert.Contains("[REDACTED]", logged.SanitizedDetails);
    }

    // 7. Mandatory Correction 8: Fail-closed on persistent rule loading failure
    [Fact]
    public async Task PersistenceLoadFailure_FailsClosedSafely()
    {
        var repo = new MockRuleRepository { ThrowOnLoad = true };
        var audit = new PermissionAuditLogger();
        var engine = new PermissionEngine(repo, audit);

        var tool = new TestTool { Id = "search_web", RiskLevel = ToolRiskLevel.Sensitive };
        var call = new ToolCall("c_err", "search_web", """{ "query": "test" }""", DateTimeOffset.UtcNow);

        // Evaluation must not crash, and must fail closed (i.e. require prompt, not assume allowed)
        var eval = await engine.EvaluateToolExecutionAsync("task_1", tool, call);
        Assert.False(eval.IsAllowed);
        Assert.True(eval.RequiresUserPrompt);
    }

    // 8. Unknown tool fails closed
    [Fact]
    public async Task UnknownTool_FailsClosedWithDenial()
    {
        var engine = new PermissionEngine();
        var call = new ToolCall("c_unk", "unknown_rogue_tool", "{}", DateTimeOffset.UtcNow);

        var eval = await engine.EvaluateToolExecutionAsync("task_1", null!, call);
        Assert.False(eval.IsAllowed);
        Assert.Contains("Tool reference is null", eval.Reason);
    }
}
