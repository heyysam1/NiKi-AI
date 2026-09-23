using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NikiAI.Core.Notifications;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;

namespace NikiAI.App.Views;

/// <summary>
/// Compact, dark-glass result popup window displaying task completion or reminder states.
/// Conforms to Niki AI design system and does not alter the companion character footprint.
/// </summary>
public partial class ResultPopupWindow : Window
{
    private readonly NotificationPayload _payload;
    private readonly Action<NotificationPayload>? _onViewResults;
    private readonly Action<NotificationPayload, string>? _onOpenArtifact;
    private readonly Action<NotificationPayload, TimeSpan>? _onSnooze;
    private readonly Action<NotificationPayload>? _onDismiss;
    private readonly DispatcherTimer _autoCloseTimer;

    public ResultPopupWindow(
        NotificationPayload payload,
        Action<NotificationPayload>? onViewResults = null,
        Action<NotificationPayload, string>? onOpenArtifact = null,
        Action<NotificationPayload, TimeSpan>? onSnooze = null,
        Action<NotificationPayload>? onDismiss = null)
    {
        InitializeComponent();

        _payload = payload ?? throw new ArgumentNullException(nameof(payload));
        _onViewResults = onViewResults;
        _onOpenArtifact = onOpenArtifact;
        _onSnooze = onSnooze;
        _onDismiss = onDismiss;

        PopulatePayload();
        PositionAtBottomRight();

        // 15-second auto-close timer
        _autoCloseTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(15)
        };
        _autoCloseTimer.Tick += (s, e) =>
        {
            _autoCloseTimer.Stop();
            Close();
        };
        _autoCloseTimer.Start();

        Closed += (s, e) => _autoCloseTimer.Stop();
    }

    private void PopulatePayload()
    {
        NotificationTitleText.Text = _payload.Title;
        SummaryText.Text = _payload.OneSentenceSummary;
        TimestampText.Text = _payload.CreatedAt.ToString("t");

        if (!string.IsNullOrEmpty(_payload.AssociatedTaskId))
        {
            TaskIdText.Text = $"Task: {_payload.AssociatedTaskId}";
        }

        // Configure State Badge
        switch (_payload.CompletionState)
        {
            case TaskCompletionState.Success:
                StateBadgeText.Text = "Success";
                StateBadgeText.Foreground = (MediaBrush)FindResource("StatusSuccessBrush");
                StateBadgeBorder.Background = new SolidColorBrush(MediaColor.FromArgb(0x22, 0x22, 0xC5, 0x5E));
                break;
            case TaskCompletionState.Failed:
                StateBadgeText.Text = "Failed";
                StateBadgeText.Foreground = (MediaBrush)FindResource("StatusErrorBrush");
                StateBadgeBorder.Background = new SolidColorBrush(MediaColor.FromArgb(0x22, 0xEF, 0x44, 0x44));
                break;
            case TaskCompletionState.Reminder:
                StateBadgeText.Text = "Reminder";
                StateBadgeText.Foreground = (MediaBrush)FindResource("PrimaryBrandBrush");
                StateBadgeBorder.Background = new SolidColorBrush(MediaColor.FromArgb(0x22, 0xF9, 0x73, 0x16));
                break;
            case TaskCompletionState.Cancelled:
                StateBadgeText.Text = "Cancelled";
                StateBadgeText.Foreground = (MediaBrush)FindResource("TextMutedBrush");
                StateBadgeBorder.Background = new SolidColorBrush(MediaColor.FromArgb(0x22, 0x7E, 0x87, 0x95));
                break;
            default:
                StateBadgeText.Text = "Info";
                StateBadgeText.Foreground = (MediaBrush)FindResource("StatusInfoBrush");
                StateBadgeBorder.Background = new SolidColorBrush(MediaColor.FromArgb(0x22, 0x60, 0xA5, 0xFA));
                break;
        }

        // Key Outputs
        if (_payload.KeyOutputs != null && _payload.KeyOutputs.Count > 0)
        {
            KeyOutputsItemsControl.ItemsSource = _payload.KeyOutputs;
            KeyOutputsItemsControl.Visibility = Visibility.Visible;
        }
        else
        {
            KeyOutputsItemsControl.Visibility = Visibility.Collapsed;
        }

        // Buttons configuration
        if (_payload.Type == NotificationType.Reminder)
        {
            SnoozeButton.Visibility = Visibility.Visible;
            ViewResultsButton.Content = "Acknowledge";
        }

        if (_payload.ArtifactLinks != null && _payload.ArtifactLinks.Count > 0)
        {
            OpenArtifactButton.Visibility = Visibility.Visible;
        }

        // Character avatar override if provided
        if (!string.IsNullOrEmpty(_payload.CharacterAvatarPath) && System.IO.File.Exists(_payload.CharacterAvatarPath))
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(_payload.CharacterAvatarPath, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                AvatarImage.Source = bitmap;
            }
            catch { }
        }
    }

    private void PositionAtBottomRight()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 16;
        Top = workArea.Bottom - Height - 16;
    }

    private void OnViewResultsClicked(object sender, RoutedEventArgs e)
    {
        _autoCloseTimer.Stop();
        _onViewResults?.Invoke(_payload);
        Close();
    }

    private void OnOpenArtifactClicked(object sender, RoutedEventArgs e)
    {
        _autoCloseTimer.Stop();
        var artifact = _payload.ArtifactLinks?.FirstOrDefault() ?? "";
        _onOpenArtifact?.Invoke(_payload, artifact);
        Close();
    }

    private void OnSnoozeClicked(object sender, RoutedEventArgs e)
    {
        _autoCloseTimer.Stop();
        _onSnooze?.Invoke(_payload, TimeSpan.FromMinutes(5));
        Close();
    }

    private void OnDismissClicked(object sender, RoutedEventArgs e)
    {
        _autoCloseTimer.Stop();
        _onDismiss?.Invoke(_payload);
        Close();
    }
}
