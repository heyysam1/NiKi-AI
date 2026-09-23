using NikiAI.Core.Widgets;

namespace NikiAI.Widgets;

/// <summary>
/// Quick Note Widget: Ephemeral desktop scratchpad for short notes.
/// Strict Privacy: Ephemeral in-memory data only.
/// Never creates persistent long-term memories, never writes to timeline,
/// and note text never enters Desktop Pet Context.
/// </summary>
public class QuickNoteWidget : BaseWidget
{
    private string _noteContent = string.Empty;

    public override string Id => "quick_note";
    public override string Title => "Quick Note";
    public override WidgetCategory Category => WidgetCategory.Productivity;
    public override string IconGlyph => "📝";

    public string NoteContent
    {
        get => _noteContent;
        set
        {
            if (SetField(ref _noteContent, value ?? string.Empty))
            {
                UpdateDisplay();
            }
        }
    }

    public QuickNoteWidget()
    {
        ActionLabel = "Clear";
        UpdateDisplay();
    }

    public override Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        UpdateDisplay();
        return Task.CompletedTask;
    }

    public override Task ExecuteActionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        NoteContent = string.Empty;
        return Task.CompletedTask;
    }

    private void UpdateDisplay()
    {
        if (string.IsNullOrWhiteSpace(_noteContent))
        {
            PrimaryDisplayValue = "Note empty";
            SecondaryDisplayValue = "0 words";
            PresentationState = WidgetPresentationState.Empty;
        }
        else
        {
            var words = _noteContent.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
            var chars = _noteContent.Length;
            var snippet = _noteContent.Replace("\r\n", " ").Replace('\n', ' ');
            if (snippet.Length > 28)
            {
                snippet = snippet[..25] + "...";
            }

            PrimaryDisplayValue = snippet;
            SecondaryDisplayValue = $"{words} word{(words == 1 ? "" : "s")}, {chars} chars";
            PresentationState = WidgetPresentationState.Active;
        }
    }
}
