using System.Diagnostics;
using System.Text.Json;
using NikiAI.Core.Tools;
using NikiAI.Core.Workflows;

namespace NikiAI.Workflows;

/// <summary>
/// Controlled agent tool allowing AgentOperator to execute registered workflows.
/// Routes execution through IWorkflowEngine, preserving tool security, audit logging,
/// and privacy-preserving lifecycle signal emission.
/// </summary>
public class RunWorkflowTool : ITool
{
    private readonly IWorkflowEngine _workflowEngine;

    public string Id => "run_workflow";
    public string Name => "Run Workflow";
    public string Description => "Executes a defined multi-step workflow by its workflow ID with optional inputs.";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Sensitive;
    public TimeSpan DefaultTimeout => TimeSpan.FromMinutes(5);

    public string InputSchemaJson => """
    {
        "type": "object",
        "required": ["workflow_id"],
        "properties": {
            "workflow_id": {
                "type": "string",
                "minLength": 1
            },
            "inputs": {
                "type": "object"
            }
        }
    }
    """;

    public RunWorkflowTool(IWorkflowEngine workflowEngine)
    {
        _workflowEngine = workflowEngine ?? throw new ArgumentNullException(nameof(workflowEngine));
    }

    public async Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(call);

        var stopwatch = Stopwatch.StartNew();

        string workflowId;
        Dictionary<string, string> inputs = new();

        try
        {
            using var doc = JsonDocument.Parse(call.ArgumentsJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("workflow_id", out var wfProp) || string.IsNullOrWhiteSpace(wfProp.GetString()))
            {
                return ToolResult.Failure(call.CallId, Id, "Missing required parameter 'workflow_id'.", stopwatch.Elapsed);
            }
            workflowId = wfProp.GetString()!;

            if (root.TryGetProperty("inputs", out var inputsProp) && inputsProp.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in inputsProp.EnumerateObject())
                {
                    inputs[prop.Name] = prop.Value.ToString();
                }
            }
        }
        catch (JsonException ex)
        {
            return ToolResult.Failure(call.CallId, Id, $"Invalid arguments JSON: {ex.Message}", stopwatch.Elapsed);
        }

        try
        {
            var runRecord = await _workflowEngine.ExecuteWorkflowAsync(workflowId, inputs, cancellationToken);
            stopwatch.Stop();

            string outputJson = JsonSerializer.Serialize(new
            {
                run_id = runRecord.RunId,
                workflow_id = runRecord.WorkflowId,
                status = runRecord.Status.ToString(),
                started_at = runRecord.StartedAt,
                completed_at = runRecord.CompletedAt,
                current_step = runRecord.CurrentStep,
                info = runRecord.SanitizedStatusInfo
            });

            if (runRecord.Status == WorkflowExecutionStatus.Completed)
            {
                return ToolResult.Success(call.CallId, Id, outputJson, stopwatch.Elapsed);
            }
            else
            {
                return ToolResult.Failure(call.CallId, Id, $"Workflow execution {runRecord.Status}: {runRecord.SanitizedStatusInfo ?? "Incomplete"}", stopwatch.Elapsed);
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return ToolResult.Failure(call.CallId, Id, $"Workflow execution threw exception: {ex.Message}", stopwatch.Elapsed);
        }
    }
}
