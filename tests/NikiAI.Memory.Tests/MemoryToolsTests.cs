using System.Text.Json;
using NikiAI.Core.Memory;
using NikiAI.Core.Security;
using NikiAI.Core.Tools;
using NikiAI.Memory;
using NikiAI.Tools;
using Xunit;

namespace NikiAI.Memory.Tests;

public sealed class MemoryToolsTests
{
    private readonly InMemoryMemoryStore _memoryStore = new();
    private readonly MemoryService _memoryService;
    private readonly MemorySaveTool _saveTool;
    private readonly MemoryQueryTool _queryTool;
    private readonly MemoryDeleteTool _deleteTool;
    private readonly MemoryClearTool _clearTool;

    public MemoryToolsTests()
    {
        _memoryService = new MemoryService(_memoryStore);
        _saveTool = new MemorySaveTool(_memoryService);
        _queryTool = new MemoryQueryTool(_memoryService);
        _deleteTool = new MemoryDeleteTool(_memoryService);
        _clearTool = new MemoryClearTool(_memoryService);
    }

    [Fact]
    public void ToolProperties_MatchSecuritySpecifications()
    {
        Assert.Equal("memory_save", _saveTool.Id);
        Assert.Equal(ToolRiskLevel.LowRiskReversible, _saveTool.RiskLevel);

        Assert.Equal("memory_query", _queryTool.Id);
        Assert.Equal(ToolRiskLevel.Informational, _queryTool.RiskLevel);

        Assert.Equal("memory_delete", _deleteTool.Id);
        Assert.Equal(ToolRiskLevel.LowRiskReversible, _deleteTool.RiskLevel);

        Assert.Equal("memory_clear", _clearTool.Id);
        Assert.Equal(ToolRiskLevel.Sensitive, _clearTool.RiskLevel);
    }

    [Fact]
    public async Task MemorySaveTool_ValidArguments_SavesMemory()
    {
        var call = new ToolCall(
            "call_save_1",
            "memory_save",
            JsonSerializer.Serialize(new
            {
                category = "LongTerm",
                context_explanation = "user_hobby",
                content = "Astronomy and stargazing"
            }),
            DateTimeOffset.UtcNow);

        var result = await _saveTool.ExecuteAsync(call);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.OutputJson);
        using var doc = JsonDocument.Parse(result.OutputJson);
        Assert.True(doc.RootElement.GetProperty("saved").GetBoolean());
        var id = doc.RootElement.GetProperty("id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(id));
    }

    [Fact]
    public async Task MemoryQueryTool_ValidQuery_ReturnsMatchedMemories()
    {
        await _memoryService.SaveExplicitMemoryAsync("San Francisco", MemoryCategory.LongTerm, "user_city");

        var call = new ToolCall(
            "call_query_1",
            "memory_query",
            JsonSerializer.Serialize(new
            {
                query = "Francisco"
            }),
            DateTimeOffset.UtcNow);

        var result = await _queryTool.ExecuteAsync(call);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.OutputJson);
        using var doc = JsonDocument.Parse(result.OutputJson);
        var items = doc.RootElement.GetProperty("memories");
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal("San Francisco", items[0].GetProperty("content").GetString());
    }

    [Fact]
    public async Task MemoryDeleteTool_ExistingId_DeletesMemory()
    {
        var item = await _memoryService.SaveExplicitMemoryAsync("val", MemoryCategory.Project, "temp");

        var call = new ToolCall(
            "call_del_1",
            "memory_delete",
            JsonSerializer.Serialize(new
            {
                id = item.Id
            }),
            DateTimeOffset.UtcNow);

        var result = await _deleteTool.ExecuteAsync(call);

        Assert.True(result.IsSuccess);
        var retrieved = await _memoryService.GetMemoryByIdAsync(item.Id);
        Assert.Null(retrieved);
    }

    [Fact]
    public async Task MemoryClearTool_Category_ClearsCategory()
    {
        await _memoryService.SaveExplicitMemoryAsync("v1", MemoryCategory.Project, "s1");
        await _memoryService.SaveExplicitMemoryAsync("v2", MemoryCategory.LongTerm, "l1");

        var call = new ToolCall(
            "call_clr_1",
            "memory_clear",
            JsonSerializer.Serialize(new
            {
                category = "Project"
            }),
            DateTimeOffset.UtcNow);

        var result = await _clearTool.ExecuteAsync(call);

        Assert.True(result.IsSuccess);
        using var doc = JsonDocument.Parse(result.OutputJson!);
        Assert.Equal(1, doc.RootElement.GetProperty("items_cleared").GetInt32());

        var projectLeft = await _memoryService.GetMemoriesAsync(MemoryCategory.Project);
        var longLeft = await _memoryService.GetMemoriesAsync(MemoryCategory.LongTerm);
        Assert.Empty(projectLeft);
        Assert.Single(longLeft);
    }
}
