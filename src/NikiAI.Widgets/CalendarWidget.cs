using NikiAI.Core.Scheduler;
using NikiAI.Core.Widgets;

namespace NikiAI.Widgets;

/// <summary>
/// Calendar Widget: Displays the next upcoming scheduled event/item and today's item count.
/// Consumes the existing IScheduledItemRepository.
/// Supports valid empty state when no upcoming items exist.
/// </summary>
public class CalendarWidget : BaseWidget
{
    private readonly IScheduledItemRepository _repository;

    public override string Id => "calendar";
    public override string Title => "Calendar";
    public override WidgetCategory Category => WidgetCategory.Productivity;
    public override string IconGlyph => "📅";

    public CalendarWidget(IScheduledItemRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        ActionLabel = "Refresh";
        PrimaryDisplayValue = "No upcoming events";
        SecondaryDisplayValue = "0 events today";
        PresentationState = WidgetPresentationState.Empty;
    }

    public override async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        try
        {
            var now = DateTimeOffset.Now;
            var today = now.Date;

            var items = await _repository.GetAllAsync(cancellationToken).ConfigureAwait(false);
            var activeItems = items
                .Where(i => (i.Status == ScheduledItemStatus.Scheduled || i.Status == ScheduledItemStatus.Snoozed) && i.ScheduledTime >= now)
                .OrderBy(i => i.ScheduledTime)
                .ToList();

            var todayCount = items.Count(i => i.ScheduledTime.LocalDateTime.Date == today);
            SecondaryDisplayValue = $"{todayCount} item{(todayCount == 1 ? "" : "s")} today";

            if (activeItems.Count > 0)
            {
                var next = activeItems[0];
                var localTime = next.ScheduledTime.ToLocalTime();
                PrimaryDisplayValue = $"{next.Title} @ {localTime:t}";
                PresentationState = WidgetPresentationState.Active;
            }
            else
            {
                PrimaryDisplayValue = "No upcoming events";
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
