using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NotificationSystem.DTOs
{
    public record CreateNotificationRequest
    {
        public string Body { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string Channel { get; set; } = string.Empty;
    }
}