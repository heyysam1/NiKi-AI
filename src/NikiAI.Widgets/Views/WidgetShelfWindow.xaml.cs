using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NikiAI.Core.Widgets;

namespace NikiAI.Widgets.Views;

/// <summary>
/// Main desktop widget shelf window.
/// Displays active widgets in a clean, compact responsive grid.
/// Lifecycle: Suspends the central WidgetRefreshCoordinator when hidden, and resumes when shown.
/// </summary>
public partial class WidgetShelfWindow : Window
{
    private readonly WidgetRegistry _registry;
    private readonly IWidgetRefreshCoordinator _coordinator;

    public WidgetShelfWindow(WidgetRegistry registry, IWidgetRefreshCoordinator coordinator)
    {
        InitializeComponent();
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));

        WidgetsItemsControl.ItemsSource = _registry.GetAllWidgets();

        IsVisibleChanged += OnWindowIsVisibleChanged;
    }

    public void ToggleVisibility()
    {
        if (Visibility == Visibility.Visible)
        {
            Hide();
        }
        else
        {
            Show();
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }
            Activate();
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Intercept close to hide instead, keeping window and state responsive
        e.Cancel = true;
        Hide();
    }

    private void OnWindowIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            _coordinator.Resume();
        }
        else
        {
            _coordinator.Suspend();
        }
    }

    private void OnTitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnCloseButtonClick(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void OnVariantSelectorSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_registry == null) return;

        if (VariantSelector.SelectedItem is ComboBoxItem item &&
            Enum.TryParse<WidgetSurfaceVariant>(item.Content.ToString(), out var variant))
        {
            foreach (var widget in _registry.GetAllWidgets())
            {
                widget.SurfaceVariant = variant;
            }
        }
    }
}
