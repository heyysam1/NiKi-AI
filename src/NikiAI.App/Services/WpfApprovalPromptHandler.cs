using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.Logging;
using NikiAI.App.Views;
using NikiAI.Core.Security;

namespace NikiAI.App.Services;

/// <summary>
/// Default WPF implementation of <see cref="IApprovalPromptHandler"/>.
/// Spawns an independent, temporary <see cref="ApprovalPromptWindow"/> on the WPF UI thread,
/// or uses an automated handler when configured for headless/automated test execution.
/// </summary>
public class WpfApprovalPromptHandler : IApprovalPromptHandler
{
    private readonly ILogger<WpfApprovalPromptHandler>? _logger;

    /// <summary>
    /// Optional hook for programmatic / automated verification or testing.
    /// When set, this delegate intercepts approval requests before displaying a UI window.
    /// </summary>
    public Func<ApprovalRequest, CancellationToken, Task<ApprovalDecisionResult?>>? AutomatedResponseProvider { get; set; }

    public WpfApprovalPromptHandler(ILogger<WpfApprovalPromptHandler>? logger = null)
    {
        _logger = logger;
    }

    public async Task<ApprovalDecisionResult> RequestApprovalAsync(ApprovalRequest request, CancellationToken cancellationToken = default)
    {
        // 1. Check if automated response is provided (e.g., during test/verification modes)
        if (AutomatedResponseProvider != null)
        {
            var automatedResult = await AutomatedResponseProvider(request, cancellationToken);
            if (automatedResult != null)
            {
                _logger?.LogInformation("Automated approval response applied for {RequestId}: {Outcome}", request.RequestId, automatedResult.Outcome);
                return automatedResult;
            }
        }

        // 2. Ensure Application and Dispatcher are active
        var app = System.Windows.Application.Current;
        if (app == null)
        {
            _logger?.LogWarning("Application.Current is null. Failing closed with Denied for {RequestId}.", request.RequestId);
            return ApprovalDecisionResult.Denied("No UI application active to display approval prompt.");
        }

        // 3. Dispatch onto UI thread to show independent ApprovalPromptWindow
        try
        {
            return await app.Dispatcher.InvokeAsync(async () =>
            {
                var promptWindow = new ApprovalPromptWindow(request);
                return await promptWindow.WaitForDecisionAsync(cancellationToken);
            }).Task.Unwrap();
        }
        catch (OperationCanceledException)
        {
            _logger?.LogInformation("Approval prompt cancelled for {RequestId}.", request.RequestId);
            return ApprovalDecisionResult.Cancelled("Approval prompt was cancelled.");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Exception while displaying ApprovalPromptWindow for {RequestId}. Failing closed.", request.RequestId);
            return ApprovalDecisionResult.Denied($"Prompt display failed: {ex.Message}");
        }
    }
}
