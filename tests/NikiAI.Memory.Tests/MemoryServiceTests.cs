using NikiAI.Core.Memory;
using NikiAI.Memory;
using Xunit;

namespace NikiAI.Memory.Tests;

public sealed class MemoryServiceTests
{
    private readonly InMemoryMemoryStore _memoryStore = new();
    private readonly MemoryService _service;

    public MemoryServiceTests()
    {
        _service = new MemoryService(_memoryStore);
    }

    [Fact]
    public async Task SaveExplicitMemoryAsync_ValidItem_SavesAndReturns()
    {
        var item = await _service.SaveExplicitMemoryAsync(
            content: "Obsidian dark mode",
            category: MemoryCategory.LongTerm,
            contextExplanation: "theme",
            isPinned: true);

        Assert.NotNull(item);
        Assert.False(string.IsNullOrWhiteSpace(item.Id));
        Assert.Equal(MemoryCategory.LongTerm, item.Category);
        Assert.Equal("theme", item.ContextExplanation);
        Assert.Equal("Obsidian dark mode", item.Content);
        Assert.True(item.IsPinned);

        var retrieved = await _service.GetMemoryByIdAsync(item.Id);
        Assert.NotNull(retrieved);
        Assert.Equal("theme", retrieved.ContextExplanation);
    }

    [Fact]
    public async Task SearchMemoriesAsync_MatchingQuery_ReturnsResults()
    {
        await _service.SaveExplicitMemoryAsync("Visual Studio Code", MemoryCategory.LongTerm, "user_editor");
        await _service.SaveExplicitMemoryAsync("PowerShell 7", MemoryCategory.LongTerm, "user_shell");

        var results = await _service.SearchMemoriesAsync("Studio");

        Assert.Single(results);
        Assert.Equal("user_editor", results[0].ContextExplanation);
    }

    [Fact]
    public async Task DeleteMemoryAsync_ExistingItem_RemovesItem()
    {
        var item = await _service.SaveExplicitMemoryAsync("temporary note", MemoryCategory.Project, "temp_fact");
        await _service.DeleteMemoryAsync(item.Id);

        var retrieved = await _service.GetMemoryByIdAsync(item.Id);
        Assert.Null(retrieved);
    }

    [Fact]
    public async Task ClearCategoryAsync_SpecifiedCategory_ClearsOnlyThatCategory()
    {
        await _service.SaveExplicitMemoryAsync("c1", MemoryCategory.Project, "k1");
        await _service.SaveExplicitMemoryAsync("c2", MemoryCategory.Project, "k2");
        await _service.SaveExplicitMemoryAsync("c3", MemoryCategory.LongTerm, "k3");

        await _service.ClearCategoryAsync(MemoryCategory.Project);

        var projectLeft = await _service.GetMemoriesAsync(MemoryCategory.Project);
        var longTermLeft = await _service.GetMemoriesAsync(MemoryCategory.LongTerm);

        Assert.Empty(projectLeft);
        Assert.Single(longTermLeft);
    }

    [Fact]
    public async Task ExportMemoriesAsync_ContainsRedactedSecrets()
    {
        await _service.SaveExplicitMemoryAsync(
            "My password is sk-test1234567890abcdef1234567890abcdef",
            MemoryCategory.LongTerm,
            "secret_note");

        var exportedJson = await _service.ExportMemoriesAsync("json");

        Assert.DoesNotContain("sk-test1234567890abcdef1234567890abcdef", exportedJson);
        Assert.Contains("[REDACTED", exportedJson);
    }
}
