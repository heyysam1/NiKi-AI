using NikiAI.Core.Scheduler;
using NikiAI.Core.Widgets;

namespace NikiAI.Widgets;

/// <summary>
/// Reminders Widget: Displays soonest upcoming reminder and active countdown.
/// Consumes the existing IScheduledItemRepository.
/// Supports valid empty state when no reminders are scheduled.
/// </summary>
public class RemindersWidget : BaseWidget
{
    private readonly IScheduledItemRepository _repository;

    public override string Id => "reminders";
    public override string Title => "Reminders";
    public override WidgetCategory Category => WidgetCategory.Productivity;
    public override string IconGlyph => "⏰";

    public RemindersWidget(IScheduledItemRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        ActionLabel = "Refresh";
        PrimaryDisplayValue = "No reminders";
        SecondaryDisplayValue = "0 active";
        PresentationState = WidgetPresentationState.Empty;
    }

    public override async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        try
        {
            var now = DateTimeOffset.Now;
            var items = await _repository.GetAllAsync(cancellationToken).ConfigureAwait(false);
            var activeReminders = items
                .Where(i => (i.Status == ScheduledItemStatus.Scheduled || i.Status == ScheduledItemStatus.Snoozed) &&
                            i.ItemType == ScheduledItemType.Reminder)
                .OrderBy(i => i.ScheduledTime)
                .ToList();

            SecondaryDisplayValue = $"{activeReminders.Count} active";

            if (activeReminders.Count > 0)
            {
                var soonest = activeReminders[0];
                var diff = soonest.ScheduledTime - now;
                var countdownText = diff.TotalMinutes switch
                {
                    < 0 => "Due now",
                    < 1 => "in < 1 min",
                    < 60 => $"in {(int)diff.TotalMinutes}m",
                    < 1440 => $"in {(int)diff.TotalHours}h",
                    _ => $"in {(int)diff.TotalDays}d"
                };

                PrimaryDisplayValue = $"{soonest.Title} ({countdownText})";
                PresentationState = WidgetPresentationState.Active;
            }
            else
            {
                PrimaryDisplayValue = "No reminders";
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
