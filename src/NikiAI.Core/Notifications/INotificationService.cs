namespace NikiAI.Core.Notifications;

public enum NotificationType
{
    Reminder = 0,
    TaskCompleted = 1,
    TaskFailed = 2,
    SystemAlert = 3
}

public enum TaskCompletionState
{
    Success = 0,
    Failed = 1,
    Cancelled = 2,
    Reminder = 3,
    Information = 4
}

public record NotificationPayload
{
    public string NotificationId { get; init; }
    public string Title { get; init; }
    public string SummaryMessage { get; init; }
    public string? AssociatedTaskId { get; init; }
    public bool ShowPopup { get; init; }
    public NotificationType Type { get; init; }
    public TaskCompletionState CompletionState { get; init; }
    public string OneSentenceSummary { get; init; }
    public IReadOnlyList<string>? KeyOutputs { get; init; }
    public IReadOnlyList<string>? ArtifactLinks { get; init; }
    public string? CharacterAvatarPath { get; init; }
    public DateTimeOffset CreatedAt { get; init; }

    public NotificationPayload(
        string notificationId,
        string title,
        string summaryMessage,
        string? associatedTaskId = null,
        bool showPopup = true,
        NotificationType type = NotificationType.Reminder,
        TaskCompletionState completionState = TaskCompletionState.Success,
        string? oneSentenceSummary = null,
        IReadOnlyList<string>? keyOutputs = null,
        IReadOnlyList<string>? artifactLinks = null,
        string? characterAvatarPath = null,
        DateTimeOffset? createdAt = null)
    {
        NotificationId = notificationId;
        Title = title;
        SummaryMessage = summaryMessage;
        AssociatedTaskId = associatedTaskId;
        ShowPopup = showPopup;
        Type = type;
        CompletionState = completionState;
        OneSentenceSummary = oneSentenceSummary ?? summaryMessage;
        KeyOutputs = keyOutputs;
        ArtifactLinks = artifactLinks;
        CharacterAvatarPath = characterAvatarPath;
        CreatedAt = createdAt ?? DateTimeOffset.UtcNow;
    }
}

/// <summary>
/// Notification service contract for Windows toasts and in-app result popups.
/// </summary>
public interface INotificationService
{
    Task ShowNotificationAsync(NotificationPayload payload, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NotificationPayload>> GetHistoryAsync(int limit = 50, CancellationToken cancellationToken = default);
    Task ClearHistoryAsync(CancellationToken cancellationToken = default);
    event Func<NotificationPayload, Task>? NotificationReceived;
}

/// <summary>
/// Abstraction for native Windows notifications (toasts, balloon tips).
/// </summary>
public interface INativeNotificationHandler
{
    Task ShowNativeNotificationAsync(NotificationPayload payload, CancellationToken cancellationToken = default);
}

/// <summary>
/// Abstraction for in-app result popup display.
/// </summary>
public interface IResultPopupHandler
{
    Task ShowResultPopupAsync(NotificationPayload payload, CancellationToken cancellationToken = default);
}
