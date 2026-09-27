using System;
using System.IO;
using System.Threading.Tasks;
using NikiAI.Core.Security;
using NikiAI.Core.Tools;
using NikiAI.Storage;
using Xunit;

namespace NikiAI.Storage.Tests;

public class SqlitePermissionRuleRepositoryTests : IDisposable
{
    private readonly string _tempDbPath;
    private readonly StorageContext _context;
    private readonly SqlitePermissionRuleRepository _repository;

    public SqlitePermissionRuleRepositoryTests()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"niki_perm_test_{Guid.NewGuid():N}.db");
        _context = new StorageContext(_tempDbPath);
        _context.InitializeAsync().GetAwaiter().GetResult();
        _repository = new SqlitePermissionRuleRepository(_context);
    }

    public void Dispose()
    {
        if (File.Exists(_tempDbPath))
        {
            try { File.Delete(_tempDbPath); } catch { }
        }
    }

    [Fact]
    public async Task SaveRule_And_LoadRules_PersistsAndRetrievesRules()
    {
        var scope1 = PermissionScopeKey.Build("open_app", "launch", "calculator");
        var scope2 = PermissionScopeKey.Build("create_reminder");

        await _repository.SaveRuleAsync(scope1, "open_app", ApprovalDecision.AlwaysAllow, ToolRiskLevel.Sensitive);
        await _repository.SaveRuleAsync(scope2, "create_reminder", ApprovalDecision.Deny, ToolRiskLevel.LowRiskReversible);

        var rules = await _repository.LoadRulesAsync();

        Assert.Equal(2, rules.Count);
        Assert.True(rules.ContainsKey(scope1));
        Assert.Equal(ApprovalDecision.AlwaysAllow, rules[scope1]);
        Assert.True(rules.ContainsKey(scope2));
        Assert.Equal(ApprovalDecision.Deny, rules[scope2]);
    }

    [Fact]
    public async Task DeleteRule_RemovesSpecificRule()
    {
        var scope1 = PermissionScopeKey.Build("open_app", "launch", "calc");
        var scope2 = PermissionScopeKey.Build("open_app", "launch", "notepad");

        await _repository.SaveRuleAsync(scope1, "open_app", ApprovalDecision.AlwaysAllow, ToolRiskLevel.Sensitive);
        await _repository.SaveRuleAsync(scope2, "open_app", ApprovalDecision.AlwaysAllow, ToolRiskLevel.Sensitive);

        var deleted = await _repository.DeleteRuleAsync(scope1);
        Assert.True(deleted);

        var rules = await _repository.LoadRulesAsync();
        Assert.Single(rules);
        Assert.False(rules.ContainsKey(scope1));
        Assert.True(rules.ContainsKey(scope2));
    }

    [Fact]
    public async Task ClearAllRules_RemovesAllStoredRules()
    {
        await _repository.SaveRuleAsync("tool_1:*:*", "tool_1", ApprovalDecision.AlwaysAllow, ToolRiskLevel.Sensitive);
        await _repository.SaveRuleAsync("tool_2:*:*", "tool_2", ApprovalDecision.Deny, ToolRiskLevel.Sensitive);

        await _repository.ClearAllRulesAsync();

        var rules = await _repository.LoadRulesAsync();
        Assert.Empty(rules);
    }

    [Fact]
    public async Task RulesPersist_AcrossStorageContextRestart()
    {
        var scope = PermissionScopeKey.Build("search_web", "query", "*");
        await _repository.SaveRuleAsync(scope, "search_web", ApprovalDecision.AlwaysAllow, ToolRiskLevel.Sensitive);

        // Simulate application restart by creating a new context pointing to the same SQLite file
        var restartedContext = new StorageContext(_tempDbPath);
        await restartedContext.InitializeAsync();
        var restartedRepo = new SqlitePermissionRuleRepository(restartedContext);

        var reloadedRules = await restartedRepo.LoadRulesAsync();
        Assert.Single(reloadedRules);
        Assert.True(reloadedRules.ContainsKey(scope));
        Assert.Equal(ApprovalDecision.AlwaysAllow, reloadedRules[scope]);
    }
}
