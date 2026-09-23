using NikiAI.Core.Tasks;
using NikiAI.Core.Widgets;

namespace NikiAI.Widgets;

/// <summary>
/// Tasks Widget: Displays active task title and pending/running status.
/// Consumes the existing ITaskRepository.
/// 
/// Strict Privacy Boundary:
/// Task titles are presented in this user-facing widget UI only.
/// Task titles, content, and descriptions must NEVER enter Desktop Pet Context,
/// persistent Memory, Timeline, or automatic agent prompt context.
/// </summary>
public class TasksWidget : BaseWidget
{
    private readonly ITaskRepository _taskRepository;

    public override string Id => "tasks";
    public override string Title => "Tasks";
    public override WidgetCategory Category => WidgetCategory.Productivity;
    public override string IconGlyph => "✓";

    public TasksWidget(ITaskRepository taskRepository)
    {
        _taskRepository = taskRepository ?? throw new ArgumentNullException(nameof(taskRepository));
        ActionLabel = "Refresh";
        PrimaryDisplayValue = "No active tasks";
        SecondaryDisplayValue = "0 pending";
        PresentationState = WidgetPresentationState.Empty;
    }

    public override async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        try
        {
            var running = await _taskRepository.GetByStatusAsync(AgentTaskStatus.Running, false, cancellationToken).ConfigureAwait(false);
            var pending = await _taskRepository.GetByStatusAsync(AgentTaskStatus.Pending, false, cancellationToken).ConfigureAwait(false);

            var totalPending = pending.Count;
            var totalRunning = running.Count;

            SecondaryDisplayValue = $"{totalRunning} running, {totalPending} pending";

            if (running.Count > 0)
            {
                var activeTask = running[0];
                PrimaryDisplayValue = $"{activeTask.Title} (Running)";
                PresentationState = WidgetPresentationState.Active;
            }
            else if (pending.Count > 0)
            {
                var nextTask = pending[0];
                PrimaryDisplayValue = $"{nextTask.Title} (Pending)";
                PresentationState = WidgetPresentationState.Active;
            }
            else
            {
                PrimaryDisplayValue = "No active tasks";
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
