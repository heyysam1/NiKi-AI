using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Color = System.Windows.Media.Color;
using NikiAI.Core.Character;

namespace NikiAI.App.Controls;

/// <summary>
/// Lightweight WPF visual overlay rendering character-native expressions.
/// Strictly non-intrusive: zero text banners, zero task titles, auto-fading, and hit-test invisible.
/// </summary>
public partial class ExpressionOverlayControl : System.Windows.Controls.UserControl
{
    public ExpressionOverlayControl()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Displays a renderable character-native visual expression above the character.
    /// </summary>
    public void ShowExpression(RenderableExpression expression)
    {
        if (expression == null) return;

        Dispatcher.InvokeAsync(() =>
        {
            var element = CreateSymbolElement(expression);
            if (element == null) return;

            // Position centered above character sprite
            var centerX = (ActualWidth > 0 ? ActualWidth : 150.0) / 2.0 + expression.OffsetX;
            var centerY = (ActualHeight > 0 ? ActualHeight : 100.0) / 2.0 - 30.0 + expression.OffsetY;

            Canvas.SetLeft(element, centerX - 12);
            Canvas.SetTop(element, centerY - 12);

            OverlayCanvas.Children.Add(element);

            // Animate float upward and fade out
            var duration = expression.Duration;
            var fadeAnimation = new DoubleAnimation(1.0, 0.0, new Duration(duration));

            fadeAnimation.Completed += (s, e) =>
            {
                OverlayCanvas.Children.Remove(element);
            };

            element.BeginAnimation(UIElement.OpacityProperty, fadeAnimation);

            if (!expression.ReducedMotion)
            {
                var moveAnimation = new DoubleAnimation(centerY - 12, centerY - 28, new Duration(duration))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                element.BeginAnimation(Canvas.TopProperty, moveAnimation);
            }
        });
    }

    private static FrameworkElement? CreateSymbolElement(RenderableExpression expression)
    {
        var size = expression.Intensity switch
        {
            ExpressionIntensity.Low => 16.0,
            ExpressionIntensity.High => 26.0,
            _ => 20.0
        };

        var path = new Path
        {
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            IsHitTestVisible = false
        };

        switch (expression.Symbol)
        {
            case ExpressionSymbol.Heart:
                path.Data = Geometry.Parse("M 12,21.35 C 12,21.35 2.5,13.5 2.5,7.5 C 2.5,4.42 4.92,2 8,2 C 9.74,2 11.41,2.81 12,4.09 C 12.59,2.81 14.26,2 16,2 C 19.08,2 21.5,4.42 21.5,7.5 C 21.5,13.5 12,21.35 12,21.35 Z");
                path.Fill = new SolidColorBrush(Color.FromRgb(255, 75, 110)); // Vibrant Pink-Red
                break;

            case ExpressionSymbol.Sparkle or ExpressionSymbol.FocusSpark:
                path.Data = Geometry.Parse("M 12,2 L 14.5,9.5 L 22,12 L 14.5,14.5 L 12,22 L 9.5,14.5 L 2,12 L 9.5,9.5 Z");
                path.Fill = expression.Symbol == ExpressionSymbol.FocusSpark
                    ? new SolidColorBrush(Color.FromRgb(0, 220, 255)) // Cyan
                    : new SolidColorBrush(Color.FromRgb(255, 215, 0)); // Gold
                break;

            case ExpressionSymbol.StarBurst:
                path.Data = Geometry.Parse("M 12,0 L 15,8 L 24,12 L 15,16 L 12,24 L 9,16 L 0,12 L 9,8 Z");
                path.Fill = new SolidColorBrush(Color.FromRgb(255, 200, 50));
                break;

            case ExpressionSymbol.SweatDrop:
                path.Data = Geometry.Parse("M 12,2 C 12,2 5,11 5,16 C 5,19.86 8.13,23 12,23 C 15.87,23 19,19.86 19,16 C 19,11 12,2 12,2 Z");
                path.Fill = new SolidColorBrush(Color.FromRgb(80, 190, 255));
                break;

            case ExpressionSymbol.Question:
                path.Data = Geometry.Parse("M 9,8 C 9,6.34 10.34,5 12,5 C 13.66,5 15,6.34 15,8 C 15,9.5 13.8,10.4 12.9,11.1 C 12.1,11.8 11.5,12.5 11.5,14 L 12.5,14 C 12.5,13.1 13,12.5 13.7,11.9 C 14.7,11.1 16,10 16,8 C 16,5.79 14.21,4 12,4 C 9.79,4 8,5.79 8,8 Z M 11.2,16.5 A 1.2,1.2 0 1 0 13.6,16.5 A 1.2,1.2 0 1 0 11.2,16.5 Z");
                path.Fill = new SolidColorBrush(Color.FromRgb(255, 175, 50));
                break;

            case ExpressionSymbol.Exclamation:
                path.Data = Geometry.Parse("M 10.5,3 L 13.5,3 L 13,15 L 11,15 Z M 10.5,18 L 13.5,18 L 13.5,21 L 10.5,21 Z");
                path.Fill = new SolidColorBrush(Color.FromRgb(255, 60, 60));
                break;

            case ExpressionSymbol.MusicalNote:
                path.Data = Geometry.Parse("M 12,3 L 20,3 L 20,7 L 14,7 L 14,15 A 3.5,3.5 0 1 1 10.5,11.5 L 12,11.5 Z");
                path.Fill = new SolidColorBrush(Color.FromRgb(160, 100, 255));
                break;

            case ExpressionSymbol.HeraldicMark:
                path.Data = Geometry.Parse("M 12,2 L 20,5 L 20,13 C 20,18 12,22 12,22 C 12,22 4,18 4,13 L 4,5 Z");
                path.Stroke = new SolidColorBrush(Color.FromRgb(220, 180, 50));
                path.StrokeThickness = 2.0;
                path.Fill = new SolidColorBrush(Color.FromArgb(120, 220, 180, 50));
                break;

            case ExpressionSymbol.DarkOrb:
                path.Data = Geometry.Parse("M 12,3 A 9,9 0 1 1 11.99,3 Z");
                path.Fill = new SolidColorBrush(Color.FromRgb(65, 30, 95));
                path.Stroke = new SolidColorBrush(Color.FromRgb(150, 70, 220));
                path.StrokeThickness = 1.5;
                break;

            default:
                return null;
        }

        return path;
    }
}
