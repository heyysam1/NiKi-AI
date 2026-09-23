using System.Text.Json;
using Microsoft.Extensions.Logging;
using NikiAI.Core.Tasks;
using NikiAI.Core.Workflows;

namespace NikiAI.Workflows;

/// <summary>
/// Constrained event trigger adapter subscribing to task lifecycle events via ITaskRepository.
/// Strictly avoids creating a generic event bus: Phase 13 event triggers are constrained to existing task lifecycle events.
/// All invocations route through IWorkflowEngine and never bypass PermissionEngine or approval requirements.
/// </summary>
public class ConstrainedEventTriggerListener
{
    private readonly ITaskRepository _taskRepository;
    private readonly IWorkflowRepository _workflowRepository;
    private readonly IWorkflowEngine _workflowEngine;
    private readonly ILogger<ConstrainedEventTriggerListener>? _logger;

    public ConstrainedEventTriggerListener(
        ITaskRepository taskRepository,
        IWorkflowRepository workflowRepository,
        IWorkflowEngine workflowEngine,
        ILogger<ConstrainedEventTriggerListener>? logger = null)
    {
        _taskRepository = taskRepository ?? throw new ArgumentNullException(nameof(taskRepository));
        _workflowRepository = workflowRepository ?? throw new ArgumentNullException(nameof(workflowRepository));
        _workflowEngine = workflowEngine ?? throw new ArgumentNullException(nameof(workflowEngine));
        _logger = logger;

        _taskRepository.StatusChanged += OnTaskStatusChangedAsync;
    }

    private void OnTaskStatusChangedAsync(string taskId, AgentTaskStatus previousStatus, AgentTaskStatus newStatus)
    {
        _ = HandleTaskTransitionAsync(taskId, previousStatus, newStatus);
    }

    private async Task HandleTaskTransitionAsync(string taskId, AgentTaskStatus previousStatus, AgentTaskStatus newStatus)
    {
        try
        {
            var workflows = await _workflowRepository.GetAllWorkflowsAsync();
            foreach (var wf in workflows)
            {
                if (!wf.IsEnabled || wf.Trigger != TriggerType.Event)
                {
                    continue;
                }

                // Check trigger config for matching event type (e.g. "task_completed" or specific status)
                var matches = false;
                if (!string.IsNullOrWhiteSpace(wf.TriggerConfigJson))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(wf.TriggerConfigJson);
                        if (doc.RootElement.TryGetProperty("target_status", out var statusProp))
                        {
                            var targetStatusStr = statusProp.GetString();
                            if (Enum.TryParse<AgentTaskStatus>(targetStatusStr, true, out var targetStatus))
                            {
                                matches = (newStatus == targetStatus);
                            }
                        }
                    }
                    catch
                    {
                        // Fallback matching
                    }
                }
                else
                {
                    // Default event trigger fires when any task completes
                    matches = (newStatus == AgentTaskStatus.Completed);
                }

                if (matches)
                {
                    _logger?.LogInformation("Task transition {Old} -> {New} for task {TaskId} triggered workflow {WorkflowId}",
                        previousStatus, newStatus, taskId, wf.Id);

                    await _workflowEngine.ExecuteWorkflowAsync(wf.Id);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error processing event trigger for task {TaskId}", taskId);
        }
    }
}
