using NotificationSystem.Models;

namespace NotificationSystem.Workers
{
    public interface INotificationProvider
    {
        Task<bool> SendAsync(Notification notification, CancellationToken cancellationToken);
    }
}