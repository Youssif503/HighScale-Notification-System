using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NotificationSystem.Models
{
    public class Notification
    {
        public string Id { set; get; } = string.Empty;
        public string body { set; get; } = string.Empty;
        public string status { set; get; } = NotificationStatus.pending.ToString();
        public string channel { set; get; } = string.Empty;
        public DateTime SendAt {set; get; } = DateTime.UtcNow;
        public string UserId { set; get; } = string.Empty;
        public User? User { set; get; } 
    }
}