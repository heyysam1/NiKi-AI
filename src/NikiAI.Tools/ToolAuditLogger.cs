using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NikiAI.Core.Tasks;
using NikiAI.Core.Tools;

namespace NikiAI.Tools;

/// <summary>
/// Structured audit logger for capturing all tool executions.
/// Supports both task-associated execution (persisting to ITaskRepository)
/// and standalone execution (capturing in memory buffer and logger).
/// </summary>
public class ToolAuditLogger : IToolAuditLogger
{
    private readonly ITaskRepository? _taskRepository;
    private readonly ILogger<ToolAuditLogger>? _logger;
    private readonly ConcurrentQueue<ToolAuditRecord> _recentRecords = new();
    private const int MaxRecentRecords = 100;

    public ToolAuditLogger(ITaskRepository? taskRepository = null, ILogger<ToolAuditLogger>? logger = null)
    {
        _taskRepository = taskRepository;
        _logger = logger;
    }

    public async Task LogExecutionAsync(ToolAuditRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        // Store in local audit trail buffer
        _recentRecords.Enqueue(record);
        while (_recentRecords.Count > MaxRecentRecords && _recentRecords.TryDequeue(out _)) { }

        _logger?.LogInformation(
            "Tool audit: Tool='{ToolId}', CallId='{CallId}', TaskId='{TaskId}', Success={Success}, Duration={DurationMs}ms",
            record.ToolId, record.CallId, record.TaskId ?? "(standalone)", record.IsSuccess, record.Duration.TotalMilliseconds);

        // If a valid TaskId is present and task repository is configured, persist as TaskEvent
        if (!string.IsNullOrWhiteSpace(record.TaskId) && _taskRepository != null)
        {
            try
            {
                var eventDetails = new
                {
                    call_id = record.CallId,
                    tool_id = record.ToolId,
                    risk_level = record.RiskLevel.ToString(),
                    sanitized_arguments = record.SanitizedArgumentsJson,
                    sanitized_output = record.SanitizedOutputJson,
                    error = record.ErrorMessage,
                    duration_ms = record.Duration.TotalMilliseconds,
                    requires_approval = record.RequiresApproval
                };

                var taskEvent = new TaskEvent(
                    taskId: record.TaskId,
                    eventType: TaskEventType.ActionExecuted,
                    message: $"Tool '{record.ToolId}' {(record.IsSuccess ? "completed successfully" : "failed")}",
                    detailsJson: JsonSerializer.Serialize(eventDetails)
                );

                await _taskRepository.AddEventAsync(taskEvent, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to persist tool audit event to TaskRepository for task '{TaskId}'.", record.TaskId);
            }
        }
    }

    public IReadOnlyList<ToolAuditRecord> GetRecentRecords(int limit = 50)
    {
        return _recentRecords.Reverse().Take(limit).ToList();
    }
}
