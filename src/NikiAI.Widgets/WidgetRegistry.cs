using System.Collections.Concurrent;
using NikiAI.Core.Widgets;

namespace NikiAI.Widgets;

/// <summary>
/// Central registry for desktop widgets.
/// Provides discovery, enumeration, and category filtering for the 12 core widgets.
/// </summary>
public class WidgetRegistry
{
    private readonly ConcurrentDictionary<string, IWidget> _widgets = new(StringComparer.OrdinalIgnoreCase);

    public int Count => _widgets.Count;

    public IReadOnlyCollection<IWidget> GetAllWidgets() => _widgets.Values.ToList().AsReadOnly();

    public IEnumerable<IWidget> GetWidgetsByCategory(WidgetCategory category) =>
        _widgets.Values.Where(w => w.Category == category);

    public IWidget? GetWidget(string id) =>
        _widgets.TryGetValue(id, out var widget) ? widget : null;

    public void RegisterWidget(IWidget widget)
    {
        ArgumentNullException.ThrowIfNull(widget);
        _widgets[widget.Id] = widget;
    }

    public bool UnregisterWidget(string id) =>
        _widgets.TryRemove(id, out _);
}
