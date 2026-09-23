using NikiAI.Core.Tools;
using NikiAI.Core.Widgets;

namespace NikiAI.Widgets;

/// <summary>
/// Clipboard Widget: Displays preview of current clipboard snippet.
/// Consumes existing IClipboardService.
/// 
/// Strict Privacy Boundary:
/// Clipboard content remains user-facing widget data only.
/// It must NEVER automatically enter Desktop Pet Context, persistent Memory,
/// Timeline, or automatic agent prompt context.
/// </summary>
public class ClipboardWidget : BaseWidget
{
    private readonly IClipboardService _clipboardService;

    public override string Id => "clipboard";
    public override string Title => "Clipboard";
    public override WidgetCategory Category => WidgetCategory.Productivity;
    public override string IconGlyph => "📋";

    public ClipboardWidget(IClipboardService clipboardService)
    {
        _clipboardService = clipboardService ?? throw new ArgumentNullException(nameof(clipboardService));
        ActionLabel = "Clear";
        PrimaryDisplayValue = "Clipboard empty";
        SecondaryDisplayValue = "0 characters";
        PresentationState = WidgetPresentationState.Empty;
    }

    public override async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        try
        {
            var text = await _clipboardService.GetTextAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(text))
            {
                PrimaryDisplayValue = "Clipboard empty";
                SecondaryDisplayValue = "0 characters";
                PresentationState = WidgetPresentationState.Empty;
            }
            else
            {
                var clean = text.Trim().Replace("\r\n", " ").Replace('\n', ' ');
                var preview = clean.Length > 28 ? clean[..25] + "..." : clean;

                PrimaryDisplayValue = preview;
                SecondaryDisplayValue = $"{text.Length} character{(text.Length == 1 ? "" : "s")}";
                PresentationState = WidgetPresentationState.Active;
            }

            HasError = false;
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = ex.Message;
            PrimaryDisplayValue = "Clipboard unavailable";
            PresentationState = WidgetPresentationState.Error;
        }
        finally
        {
            IsLoading = false;
        }
    }

    public override async Task ExecuteActionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _clipboardService.SetTextAsync(string.Empty, cancellationToken).ConfigureAwait(false);
            PrimaryDisplayValue = "Clipboard empty";
            SecondaryDisplayValue = "0 characters";
            PresentationState = WidgetPresentationState.Empty;
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = ex.Message;
        }
    }
}
