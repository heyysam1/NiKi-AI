using NikiAI.Core.Agent;
using NikiAI.Core.Tasks;
using NikiAI.Core.Widgets;

namespace NikiAI.Widgets;

/// <summary>
/// AI Task Progress Widget: Displays active AI agent execution status.
/// Consumes existing IAgentProvider and ITaskRepository.
/// Supports valid empty state when the agent is idle.
/// </summary>
public class AiTaskProgressWidget : BaseWidget
{
    private readonly IAgentProvider _provider;
    private readonly ITaskRepository _taskRepository;

    public override string Id => "ai_task_progress";
    public override string Title => "AI Task Progress";
    public override WidgetCategory Category => WidgetCategory.AI;
    public override string IconGlyph => "⚡";

    public AiTaskProgressWidget(IAgentProvider provider, ITaskRepository taskRepository)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _taskRepository = taskRepository ?? throw new ArgumentNullException(nameof(taskRepository));

        ActionLabel = "Refresh";
        PrimaryDisplayValue = "Agent Idle";
        SecondaryDisplayValue = $"{_provider.ProviderName} • Ready";
        PresentationState = WidgetPresentationState.Empty;
    }

    public override async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        try
        {
            var running = await _taskRepository.GetByStatusAsync(AgentTaskStatus.Running, false, cancellationToken).ConfigureAwait(false);
            if (running.Count > 0)
            {
                var activeTask = running[0];
                PrimaryDisplayValue = $"Running: {activeTask.Title}";
                SecondaryDisplayValue = $"{_provider.ProviderName} • Active";
                PresentationState = WidgetPresentationState.Active;
            }
            else
            {
                PrimaryDisplayValue = "Agent Idle";
                SecondaryDisplayValue = $"{_provider.ProviderName} • Ready";
                PresentationState = WidgetPresentationState.Empty;
            }

            HasError = false;
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = ex.Message;
            PresentationState = WidgetPresentationState.Error;
        }
        finally
        {
            IsLoading = false;
        }
    }

    public override Task ExecuteActionAsync(CancellationToken cancellationToken = default)
    {
        return RefreshAsync(cancellationToken);
    }
}
