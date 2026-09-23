using System.Text.Json;
using Microsoft.Extensions.Logging;
using NikiAI.Core.Scheduler;
using NikiAI.Core.Workflows;

namespace NikiAI.Workflows;

/// <summary>
/// Adapts scheduled trigger events from ISchedulerService to execute workflows via IWorkflowEngine.
/// Zero duplicate scheduler systems: reuses existing ISchedulerService and ScheduledItem.
/// Scheduled execution never bypasses PermissionEngine or approval requirements.
/// </summary>
public class ScheduledWorkflowTriggerListener
{
    private readonly ISchedulerService _schedulerService;
    private readonly IWorkflowEngine _workflowEngine;
    private readonly ILogger<ScheduledWorkflowTriggerListener>? _logger;

    public ScheduledWorkflowTriggerListener(
        ISchedulerService schedulerService,
        IWorkflowEngine workflowEngine,
        ILogger<ScheduledWorkflowTriggerListener>? logger = null)
    {
        _schedulerService = schedulerService ?? throw new ArgumentNullException(nameof(schedulerService));
        _workflowEngine = workflowEngine ?? throw new ArgumentNullException(nameof(workflowEngine));
        _logger = logger;

        _schedulerService.ItemTriggered += OnItemTriggeredAsync;
    }

    public Task HandleItemTriggeredAsync(ScheduledItem item) => OnItemTriggeredAsync(item);

    private async Task OnItemTriggeredAsync(ScheduledItem item)
    {
        try
        {
            string? workflowId = null;

            // Check PayloadJson for workflow_id
            if (!string.IsNullOrWhiteSpace(item.PayloadJson))
            {
                try
                {
                    using var doc = JsonDocument.Parse(item.PayloadJson);
                    if (doc.RootElement.TryGetProperty("workflow_id", out var wfProp))
                    {
                        workflowId = wfProp.GetString();
                    }
                }
                catch
                {
                    // Not a json payload or not a workflow schedule
                }
            }

            // Fallback to AssociatedTaskId if formatted as workflow id
            if (string.IsNullOrWhiteSpace(workflowId) && !string.IsNullOrWhiteSpace(item.AssociatedTaskId) && item.AssociatedTaskId.StartsWith("wf-"))
            {
                workflowId = item.AssociatedTaskId;
            }

            if (!string.IsNullOrWhiteSpace(workflowId))
            {
                _logger?.LogInformation("Scheduled trigger fired for workflow {WorkflowId} (ScheduledItem: {ItemId})", workflowId, item.Id);
                await _workflowEngine.ExecuteWorkflowAsync(workflowId);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error processing scheduled workflow trigger for item {ItemId}", item.Id);
        }
    }
}
