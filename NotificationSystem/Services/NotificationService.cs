using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NotificationSystem.Data;
using NotificationSystem.DTOs;
using NotificationSystem.Models;

namespace NotificationSystem.Services
{
    public class NotificationService
    {
        private readonly ApplicationDbContext _context;
        private readonly INotificationPublisher _rabbitMq;
        public NotificationService(ApplicationDbContext context, INotificationPublisher rabbitMq)
        {
            _context = context;
            _rabbitMq = rabbitMq;
        }
        public async Task<bool> NotifyAsync(CreateNotificationRequest request)
        {
            // 1 Create Notification Object
            Notification notification = new Notification
            {
                Id = Guid.NewGuid().ToString(),
                body = request.Body,
                UserId = request.UserId,
                channel = request.Channel,
                status = NotificationStatus.pending.ToString(),
                SendAt = DateTime.UtcNow,
            };
            await _context.Notifications.AddAsync(notification);
            int isSaved = await _context.SaveChangesAsync();

            if (isSaved == 0)
            {
                return false;
            }
            // is Alredy Saved
            //2 Save It to Message Queue
            await _rabbitMq.PublishAsync(notification.Id);
            
            return true;
        }
    }
}
