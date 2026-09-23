using System.Diagnostics;
using System.Text.Json;
using NikiAI.Core.Scheduler;
using NikiAI.Core.Tools;

namespace NikiAI.Tools;

/// <summary>
/// Tool for creating low-risk user reminders.
/// Integrates with ISchedulerService when available while remaining backward compatible.
/// Computes due time, validates target time, and generates a structured reminder confirmation.
/// </summary>
public class CreateReminderTool : ITool
{
    public const string ToolId = "create_reminder";

    public string Id => ToolId;
    public string Name => "Create Reminder";
    public string Description => "Creates a reminder with a message and relative delay or scheduled due time.";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.LowRiskReversible;
    public TimeSpan DefaultTimeout => TimeSpan.FromSeconds(5);

    public string InputSchemaJson => """
    {
      "type": "object",
      "required": ["message"],
      "properties": {
        "message": { "type": "string", "minLength": 1 },
        "delay_seconds": { "type": "integer", "minimum": 1, "maximum": 86400 },
        "due_time": { "type": "string", "format": "date-time" },
        "title": { "type": "string" }
      }
    }
    """;

    private readonly Func<DateTimeOffset>? _timeProvider;
    private readonly ISchedulerService? _schedulerService;

    public CreateReminderTool(Func<DateTimeOffset>? timeProvider = null, ISchedulerService? schedulerService = null)
    {
        _timeProvider = timeProvider;
        _schedulerService = schedulerService;
    }

    public async Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var doc = JsonDocument.Parse(call.ArgumentsJson);
            var root = doc.RootElement;

            var message = root.GetProperty("message").GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(message))
            {
                stopwatch.Stop();
                return ToolResult.Failure(call.CallId, Id, "Reminder message cannot be empty.", stopwatch.Elapsed);
            }

            string title = "Reminder";
            if (root.TryGetProperty("title", out var titleElem) && titleElem.ValueKind == JsonValueKind.String)
            {
                var customTitle = titleElem.GetString()?.Trim();
                if (!string.IsNullOrEmpty(customTitle))
                {
                    title = customTitle;
                }
            }

            var now = _timeProvider?.Invoke() ?? DateTimeOffset.UtcNow;
            DateTimeOffset dueAt;

            if (root.TryGetProperty("due_time", out var dueTimeElem) && dueTimeElem.ValueKind == JsonValueKind.String)
            {
                var dueTimeStr = dueTimeElem.GetString();
                if (!DateTimeOffset.TryParse(dueTimeStr, out dueAt))
                {
                    stopwatch.Stop();
                    return ToolResult.Failure(call.CallId, Id, $"Invalid due_time format '{dueTimeStr}'. Expected ISO 8601.", stopwatch.Elapsed);
                }

                if (dueAt <= now)
                {
                    stopwatch.Stop();
                    return ToolResult.Failure(call.CallId, Id, "Reminder due_time must be in the future.", stopwatch.Elapsed);
                }
            }
            else if (root.TryGetProperty("delay_seconds", out var delayElem) && delayElem.TryGetInt32(out var delaySeconds))
            {
                if (delaySeconds <= 0)
                {
                    stopwatch.Stop();
                    return ToolResult.Failure(call.CallId, Id, "delay_seconds must be a positive integer.", stopwatch.Elapsed);
                }
                dueAt = now.AddSeconds(delaySeconds);
            }
            else
            {
                // Default to 60 seconds if neither explicit due_time nor delay_seconds was specified
                dueAt = now.AddSeconds(60);
            }

            var reminderId = Guid.NewGuid().ToString("N");

            if (_schedulerService != null)
            {
                var scheduledItem = new ScheduledItem(
                    id: reminderId,
                    title: title,
                    scheduledTime: dueAt,
                    isRecurring: false,
                    recurrenceInterval: null,
                    associatedTaskId: null,
                    description: message,
                    itemType: ScheduledItemType.Reminder,
                    status: ScheduledItemStatus.Scheduled,
                    payloadJson: call.ArgumentsJson,
                    createdAt: now,
                    updatedAt: now
                );

                await _schedulerService.ScheduleAsync(scheduledItem, cancellationToken);
            }

            var output = new
            {
                reminder_id = reminderId,
                title = title,
                message = message,
                due_time = dueAt.ToString("o"),
                created_at = now.ToString("o"),
                status = "Scheduled"
            };

            stopwatch.Stop();
            return ToolResult.Success(call.CallId, Id, JsonSerializer.Serialize(output), stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            return ToolResult.Failure(call.CallId, Id, "Tool execution was cancelled.", stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return ToolResult.Failure(call.CallId, Id, $"Failed to create reminder: {ex.Message}", stopwatch.Elapsed);
        }
    }
}
