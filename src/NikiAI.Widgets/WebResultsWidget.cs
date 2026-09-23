using NikiAI.Core.Tools;
using NikiAI.Core.Widgets;

namespace NikiAI.Widgets;

/// <summary>
/// Research / Web Results Widget: Displays latest research query and headline result.
/// Consumes the existing IWebSearchService without introducing external scraper services.
/// Correctly represents valid "No recent searches" empty state.
/// </summary>
public class WebResultsWidget : BaseWidget
{
    private readonly IWebSearchService _searchService;
    private string _lastQuery = string.Empty;

    public override string Id => "web_results";
    public override string Title => "Research / Web Results";
    public override WidgetCategory Category => WidgetCategory.Information;
    public override string IconGlyph => "🔍";

    public WebResultsWidget(IWebSearchService searchService)
    {
        _searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));
        ActionLabel = "Refresh";
        PrimaryDisplayValue = "No recent searches";
        SecondaryDisplayValue = "Ready to search";
        PresentationState = WidgetPresentationState.Empty;
    }

    public void SetLastQuery(string query)
    {
        _lastQuery = query ?? string.Empty;
    }

    public override async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_lastQuery))
        {
            PrimaryDisplayValue = "No recent searches";
            SecondaryDisplayValue = "Ready to search";
            PresentationState = WidgetPresentationState.Empty;
            return;
        }

        IsLoading = true;
        try
        {
            var response = await _searchService.SearchAsync(_lastQuery, 3, cancellationToken).ConfigureAwait(false);
            if (response.Results.Count > 0)
            {
                var top = response.Results[0];
                PrimaryDisplayValue = top.Title;
                SecondaryDisplayValue = $"{response.TotalCount} results for '{_lastQuery}'";
                PresentationState = WidgetPresentationState.Active;
            }
            else
            {
                PrimaryDisplayValue = $"No results for '{_lastQuery}'";
                SecondaryDisplayValue = "Try another query";
                PresentationState = WidgetPresentationState.Empty;
            }

            HasError = false;
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = ex.Message;
            PrimaryDisplayValue = "Search unavailable";
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
