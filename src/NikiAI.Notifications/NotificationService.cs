using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using NikiAI.Core.Logging;
using NikiAI.Core.Notifications;

namespace NikiAI.Notifications;

/// <summary>
/// Production notification service managing Windows native notifications, in-app result popups,
/// secret redaction, rapid-trigger deduplication, and SQLite history persistence.
/// </summary>
public class NotificationService : INotificationService
{
    private readonly INotificationRepository? _repository;
    private readonly INativeNotificationHandler? _nativeHandler;
    private readonly IResultPopupHandler? _popupHandler;
    private readonly Func<DateTimeOffset> _timeProvider;
    private readonly ILogger<NotificationService>? _logger;

    // Deduplication tracking: key -> timestamp of last dispatch
    private readonly ConcurrentDictionary<string, DateTimeOffset> _recentDispatches = new();
    private static readonly TimeSpan DeduplicationWindow = TimeSpan.FromSeconds(2);

    public event Func<NotificationPayload, Task>? NotificationReceived;

    public NotificationService(
        INotificationRepository? repository = null,
        INativeNotificationHandler? nativeHandler = null,
        IResultPopupHandler? popupHandler = null,
        Func<DateTimeOffset>? timeProvider = null,
        ILogger<NotificationService>? logger = null)
    {
        _repository = repository;
        _nativeHandler = nativeHandler;
        _popupHandler = popupHandler;
        _timeProvider = timeProvider ?? (() => DateTimeOffset.UtcNow);
        _logger = logger;
    }

    public async Task ShowNotificationAsync(NotificationPayload payload, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var now = _timeProvider();

        // 1. Secret Redaction across all user-facing and logged text
        var sanitized = SanitizePayload(payload, now);

        // 2. Rapid-trigger Deduplication check
        var dedupKey = $"{sanitized.Title}|{sanitized.SummaryMessage}|{sanitized.AssociatedTaskId}";
        if (_recentDispatches.TryGetValue(dedupKey, out var lastSent))
        {
            if (now - lastSent < DeduplicationWindow)
            {
                _logger?.LogDebug("Skipping duplicate notification within deduplication window: {Key}", dedupKey);
                return;
            }
        }
        _recentDispatches[dedupKey] = now;
        CleanupRecentDispatches(now);

        _logger?.LogInformation("Dispatching notification {Id}: '{Title}' (Popup={ShowPopup})",
            sanitized.NotificationId, sanitized.Title, sanitized.ShowPopup);

        // 3. Persist to SQLite Notification History
        if (_repository != null)
        {
            try
            {
                await _repository.SaveAsync(sanitized, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to persist notification {Id} to history.", sanitized.NotificationId);
            }
        }

        // 4. Dispatch to Native Windows Notification Handler (Tray Balloon / Toast)
        if (_nativeHandler != null)
        {
            try
            {
                await _nativeHandler.ShowNativeNotificationAsync(sanitized, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Native notification handler failed for notification {Id}", sanitized.NotificationId);
            }
        }

        // 5. Dispatch to In-App Result Popup Handler (WPF ResultPopupWindow)
        if (sanitized.ShowPopup && _popupHandler != null)
        {
            try
            {
                await _popupHandler.ShowResultPopupAsync(sanitized, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Result popup handler failed for notification {Id}", sanitized.NotificationId);
            }
        }

        // 6. Raise NotificationReceived Event
        if (NotificationReceived != null)
        {
            try
            {
                await NotificationReceived.Invoke(sanitized);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in NotificationReceived event subscriber for {Id}", sanitized.NotificationId);
            }
        }
    }

    public async Task<IReadOnlyList<NotificationPayload>> GetHistoryAsync(int limit = 50, CancellationToken cancellationToken = default)
    {
        if (_repository != null)
        {
            return await _repository.GetHistoryAsync(limit, cancellationToken);
        }

        return Array.Empty<NotificationPayload>();
    }

    public async Task ClearHistoryAsync(CancellationToken cancellationToken = default)
    {
        if (_repository != null)
        {
            await _repository.ClearAsync(cancellationToken);
        }
    }

    private static NotificationPayload SanitizePayload(NotificationPayload payload, DateTimeOffset now)
    {
        var redactedTitle = SecretRedactor.Redact(payload.Title);
        var redactedSummary = SecretRedactor.Redact(payload.SummaryMessage);
        var redactedOneSentence = SecretRedactor.Redact(payload.OneSentenceSummary);

        IReadOnlyList<string>? redactedOutputs = null;
        if (payload.KeyOutputs != null)
        {
            redactedOutputs = payload.KeyOutputs.Select(s => SecretRedactor.Redact(s)).ToList().AsReadOnly();
        }

        return payload with
        {
            Title = redactedTitle,
            SummaryMessage = redactedSummary,
            OneSentenceSummary = redactedOneSentence,
            KeyOutputs = redactedOutputs,
            CreatedAt = payload.CreatedAt == default ? now : payload.CreatedAt
        };
    }

    private void CleanupRecentDispatches(DateTimeOffset now)
    {
        if (_recentDispatches.Count > 50)
        {
            foreach (var kvp in _recentDispatches)
            {
                if (now - kvp.Value > DeduplicationWindow * 3)
                {
                    _recentDispatches.TryRemove(kvp.Key, out _);
                }
            }
        }
    }
}
