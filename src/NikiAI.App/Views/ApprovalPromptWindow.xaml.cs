using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using NikiAI.Core.Security;
using NikiAI.Core.Tools;

namespace NikiAI.App.Views;

/// <summary>
/// Independent temporary modal window for user permission approval.
/// Never attaches to or encloses the companion window (preserves free-floating companion).
/// Race-safe completion ensures exactly one terminal decision: Approved, Denied, TimedOut, or Cancelled.
/// </summary>
public partial class ApprovalPromptWindow : Window
{
    private readonly ApprovalRequest _request;
    private readonly TaskCompletionSource<ApprovalDecisionResult> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly DispatcherTimer _countdownTimer;
    private readonly DateTimeOffset _expiresAt;
    private int _isCompleted; // 0 = pending, 1 = completed

    public ApprovalPromptWindow(ApprovalRequest request)
    {
        InitializeComponent();

        _request = request ?? throw new ArgumentNullException(nameof(request));
        _expiresAt = request.ExpiresAt;

        PopulateRequestDetails();

        _countdownTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _countdownTimer.Tick += CountdownTimer_Tick;
        _countdownTimer.Start();

        Closing += ApprovalPromptWindow_Closing;
    }

    /// <summary>
    /// Displays the prompt and returns the user's decision result asynchronously.
    /// Handles expiration and cancellation tokens safely.
    /// </summary>
    public async Task<ApprovalDecisionResult> WaitForDecisionAsync(CancellationToken cancellationToken = default)
    {
        using var registration = cancellationToken.Register(() =>
        {
            if (TryComplete(ApprovalDecisionResult.Cancelled("User cancelled operation")))
            {
                Dispatcher.InvokeAsync(Close);
            }
        });

        Show();
        Activate();

        try
        {
            return await _tcs.Task;
        }
        finally
        {
            _countdownTimer.Stop();
        }
    }

    private void PopulateRequestDetails()
    {
        SubtitleText.Text = $"Action requested by '{_request.ToolId}'";
        ActionDescriptionText.Text = _request.ActionDescription;
        ResourceText.Text = string.IsNullOrWhiteSpace(_request.AffectedResource) ? "(Local / None)" : _request.AffectedResource;
        ReversibleText.Text = _request.IsReversible ? "Yes" : "No";

        // Risk Level Badge Styling
        switch (_request.RiskLevel)
        {
            case ToolRiskLevel.HighRisk:
                RiskBadgeText.Text = "High Risk";
                RiskBadgeText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xEF, 0x44, 0x44)); // Red
                RiskBadgeBorder.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x28, 0xEF, 0x44, 0x44));
                // High-risk actions cannot have persistent AlwaysAllow rules (Security Specification)
                AlwaysAllowButton.Visibility = Visibility.Collapsed;
                break;

            case ToolRiskLevel.Sensitive:
                RiskBadgeText.Text = "Sensitive";
                RiskBadgeText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF5, 0x9E, 0x0B)); // Amber
                RiskBadgeBorder.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x28, 0xF5, 0x9E, 0x0B));
                AlwaysAllowButton.Visibility = Visibility.Visible;
                break;

            case ToolRiskLevel.LowRiskReversible:
            case ToolRiskLevel.Informational:
            default:
                RiskBadgeText.Text = "Low Risk";
                RiskBadgeText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x22, 0xC5, 0x5E)); // Green
                RiskBadgeBorder.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x28, 0x22, 0xC5, 0x5E));
                AlwaysAllowButton.Visibility = Visibility.Visible;
                break;
        }

        // External Data Disclosure Warning
        if (!string.IsNullOrWhiteSpace(_request.ExternalDataDisclosureExplanation))
        {
            DisclosurePanel.Visibility = Visibility.Visible;
            DisclosureText.Text = _request.ExternalDataDisclosureExplanation;
        }
        else
        {
            DisclosurePanel.Visibility = Visibility.Collapsed;
        }

        UpdateExpirationText();
    }

    private void CountdownTimer_Tick(object? sender, EventArgs e)
    {
        var remaining = (int)Math.Max(0, (_expiresAt - DateTimeOffset.UtcNow).TotalSeconds);
        if (remaining <= 0)
        {
            _countdownTimer.Stop();
            if (TryComplete(ApprovalDecisionResult.TimedOut("Approval request timed out")))
            {
                Close();
            }
            return;
        }

        UpdateExpirationText();
    }

    private void UpdateExpirationText()
    {
        var remaining = (int)Math.Max(0, (_expiresAt - DateTimeOffset.UtcNow).TotalSeconds);
        ExpirationText.Text = $"Request expires in {remaining}s";
    }

    private void DenyButton_Click(object sender, RoutedEventArgs e)
    {
        if (TryComplete(ApprovalDecisionResult.Denied("User denied request")))
        {
            Close();
        }
    }

    private void AllowOnceButton_Click(object sender, RoutedEventArgs e)
    {
        if (TryComplete(ApprovalDecisionResult.Approved(ApprovalDecision.AllowOnce, "User approved for single invocation")))
        {
            Close();
        }
    }

    private void AlwaysAllowButton_Click(object sender, RoutedEventArgs e)
    {
        if (_request.RiskLevel == ToolRiskLevel.HighRisk)
        {
            // Failsafe in case button was shown: HighRisk cannot be AlwaysAllow
            if (TryComplete(ApprovalDecisionResult.Denied("Policy Violation: High-risk operations cannot be granted blanket AlwaysAllow.")))
            {
                Close();
            }
            return;
        }

        if (TryComplete(ApprovalDecisionResult.Approved(ApprovalDecision.AlwaysAllow, "User granted persistent permission")))
        {
            Close();
        }
    }

    private void ApprovalPromptWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _countdownTimer.Stop();
        // If window is closing and no outcome has been set (e.g. user pressed Alt+F4 or closed window), fail closed with Denied
        TryComplete(ApprovalDecisionResult.Denied("User dismissed approval prompt"));
    }

    private bool TryComplete(ApprovalDecisionResult result)
    {
        if (Interlocked.CompareExchange(ref _isCompleted, 1, 0) == 0)
        {
            _tcs.TrySetResult(result);
            return true;
        }
        return false;
    }
}
