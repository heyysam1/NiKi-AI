namespace NikiAI.Core.Notifications;

/// <summary>
/// Contract for persistent storage and querying of notification history.
/// </summary>
public interface INotificationRepository
{
    Task SaveAsync(NotificationPayload notification, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NotificationPayload>> GetHistoryAsync(int limit = 50, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
}
