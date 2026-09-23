using System.Diagnostics;
using System.Text.Json;
using NikiAI.Core.Memory;
using NikiAI.Core.Tools;

namespace NikiAI.Tools;

/// <summary>
/// Tool for deleting a specific stored memory item by its unique identifier.
/// Requires explicit user intent.
/// </summary>
public class MemoryDeleteTool : ITool
{
    public const string ToolId = "memory_delete";

    public string Id => ToolId;
    public string Name => "Delete Memory";
    public string Description => "Deletes a specific memory item by its unique ID. Requires explicit user intent.";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.LowRiskReversible;
    public TimeSpan DefaultTimeout => TimeSpan.FromSeconds(10);

    public string InputSchemaJson => """
    {
      "type": "object",
      "properties": {
        "id": { "type": "string", "description": "The unique ID of the memory item to delete." }
      },
      "required": ["id"]
    }
    """;

    private readonly IMemoryService _memoryService;

    public MemoryDeleteTool(IMemoryService memoryService)
    {
        _memoryService = memoryService ?? throw new ArgumentNullException(nameof(memoryService));
    }

    public async Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (!_memoryService.IsMemoryEnabled())
            {
                stopwatch.Stop();
                return ToolResult.Failure(call.CallId, Id, "Cannot delete memory: Memory is disabled by privacy settings.", stopwatch.Elapsed);
            }

            if (string.IsNullOrWhiteSpace(call.ArgumentsJson))
            {
                stopwatch.Stop();
                return ToolResult.Failure(call.CallId, Id, "Missing arguments JSON.", stopwatch.Elapsed);
            }

            using var doc = JsonDocument.Parse(call.ArgumentsJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("id", out var idElem) || string.IsNullOrWhiteSpace(idElem.GetString()))
            {
                stopwatch.Stop();
                return ToolResult.Failure(call.CallId, Id, "Missing required argument 'id'.", stopwatch.Elapsed);
            }

            var memoryId = idElem.GetString()!;
            var existing = await _memoryService.GetMemoryByIdAsync(memoryId, cancellationToken);
            if (existing == null)
            {
                stopwatch.Stop();
                return ToolResult.Failure(call.CallId, Id, $"Memory item with ID '{memoryId}' was not found.", stopwatch.Elapsed);
            }

            await _memoryService.DeleteMemoryAsync(memoryId, cancellationToken);
            stopwatch.Stop();

            var responseJson = JsonSerializer.Serialize(new
            {
                deleted = true,
                id = memoryId,
                content_preview = existing.Content.Length > 40 ? existing.Content[..40] + "..." : existing.Content
            });

            return ToolResult.Success(call.CallId, Id, responseJson, stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return ToolResult.Failure(call.CallId, Id, $"Failed to delete memory: {ex.Message}", stopwatch.Elapsed);
        }
    }
}
