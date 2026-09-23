using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NikiAI.Core.Widgets;

namespace NikiAI.Widgets.Controls;

/// <summary>
/// Reusable UI control for rendering any IWidget.
/// Implements the shared widget language (Icon, Title, Category, Primary Metric, Secondary Data, Action).
/// Dynamically renders Glass, Solid, and Minimal surface variants with standard tokens.
/// </summary>
public partial class WidgetCard : UserControl
{
    public WidgetCard()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is IWidget widget)
        {
            ApplySurfaceVariant(widget.SurfaceVariant);
        }
    }

    public void ApplySurfaceVariant(WidgetSurfaceVariant variant)
    {
        switch (variant)
        {
            case WidgetSurfaceVariant.Glass:
                CardBorderContainer.Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x15, 0x19, 0x22));
                CardBorderContainer.BorderBrush = new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF));
                CardBorderContainer.CornerRadius = new CornerRadius(12);
                break;

            case WidgetSurfaceVariant.Solid:
                CardBorderContainer.Background = new SolidColorBrush(Color.FromRgb(0x15, 0x19, 0x22));
                CardBorderContainer.BorderBrush = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
                CardBorderContainer.CornerRadius = new CornerRadius(12);
                break;

            case WidgetSurfaceVariant.Minimal:
                CardBorderContainer.Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x0C, 0x10));
                CardBorderContainer.BorderBrush = new SolidColorBrush(Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF));
                CardBorderContainer.CornerRadius = new CornerRadius(8);
                break;
        }
    }

    private async void OnActionButtonClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is IWidget widget)
        {
            try
            {
                await widget.ExecuteActionAsync();
            }
            catch
            {
                // Action error handled inside widget view-model
            }
        }
    }
}
