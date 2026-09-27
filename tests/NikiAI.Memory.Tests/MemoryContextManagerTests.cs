using NikiAI.Core.Memory;
using NikiAI.Memory;
using Xunit;

namespace NikiAI.Memory.Tests;

public sealed class MemoryContextManagerTests
{
    private readonly InMemoryMemoryStore _memoryStore = new();
    private readonly MemoryService _memoryService;
    private readonly MemoryContextManager _contextManager;

    public MemoryContextManagerTests()
    {
        _memoryService = new MemoryService(_memoryStore);
        _contextManager = new MemoryContextManager(_memoryService);
    }

    [Fact]
    public async Task BuildMemoryContextPromptAsync_WithMemories_FormatsPromptSection()
    {
        await _memoryService.SaveExplicitMemoryAsync("Alex", MemoryCategory.LongTerm, "user_name");
        await _memoryService.SaveExplicitMemoryAsync("VS Code", MemoryCategory.LongTerm, "preferred_ide", isPinned: true);

        var prompt = await _contextManager.BuildMemoryContextPromptAsync(null, 500);

        Assert.Contains("<user_memory_context>", prompt);
        Assert.Contains("[Preferences & User Facts]", prompt);
        Assert.Contains("Alex", prompt);
        Assert.Contains("VS Code", prompt);
    }

    [Fact]
    public async Task BuildMemoryContextPromptAsync_WithProjectId_IncludesProjectNotes()
    {
        var projId = "proj-123";
        await _memoryService.SaveExplicitMemoryAsync("Final report v2", MemoryCategory.Project, "deliverable", projectId: projId);

        var prompt = await _contextManager.BuildMemoryContextPromptAsync(projId, 500);

        Assert.Contains("[Active Project Notes]", prompt);
        Assert.Contains("Final report v2", prompt);
    }

    [Fact]
    public async Task BuildMemoryContextPromptAsync_WhenMemoryDisabled_SuppressesInjection()
    {
        await _memoryService.SaveExplicitMemoryAsync("secret fact", MemoryCategory.LongTerm, "sensitive_fact");
        await _memoryService.SetMemoryEnabledAsync(false);

        var prompt = await _contextManager.BuildMemoryContextPromptAsync(null, 500);

        Assert.Empty(prompt);
        Assert.DoesNotContain("secret fact", prompt);
    }

    [Fact]
    public async Task BuildMemoryContextPromptAsync_RespectsMaxCharactersBudget()
    {
        for (int i = 0; i < 50; i++)
        {
            await _memoryService.SaveExplicitMemoryAsync(new string('X', 100), MemoryCategory.LongTerm, $"fact_{i}");
        }

        var prompt = await _contextManager.BuildMemoryContextPromptAsync(null, 500);

        Assert.True(prompt.Length <= 800);
    }
}
