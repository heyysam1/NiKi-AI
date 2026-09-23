namespace NikiAI.Core.Widgets;

/// <summary>
/// Widget categories as specified in 02_FEATURES_AND_SCOPE.md.
/// </summary>
public enum WidgetCategory
{
    System,
    Productivity,
    AI,
    Information,
    Media
}

/// <summary>
/// Widget surface variants as defined in 03_DESIGN_SYSTEM.md.
/// </summary>
public enum WidgetSurfaceVariant
{
    Glass,
    Solid,
    Minimal
}

/// <summary>
/// Widget contract for all Niki AI desktop widgets.
/// Follows the shared widget language: icon, title, one primary value, optional secondary data, one small action.
/// </summary>
public interface IWidget
{
    string Id { get; }
    string Title { get; }
    WidgetCategory Category { get; }
    WidgetSurfaceVariant SurfaceVariant { get; set; }
    WidgetSizeOption SizeOption { get; set; }
    bool IsVisible { get; set; }

    WidgetPresentationState PresentationState { get; }
    string? PrimaryDisplayValue { get; }
    string? SecondaryDisplayValue { get; }
    string? ActionLabel { get; }
    bool IsActionEnabled { get; }

    event EventHandler? StateChanged;

    Task RefreshAsync(CancellationToken cancellationToken = default);
    Task ExecuteActionAsync(CancellationToken cancellationToken = default);
}
