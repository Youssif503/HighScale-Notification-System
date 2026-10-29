using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NotificationSystem.DTOs;
using NotificationSystem.Services;

namespace NotificationSystem.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class NotificationController : ControllerBase
    {
        private readonly NotificationService _NotificationService;
        public NotificationController(NotificationService notificationService)
        {
            _NotificationService = notificationService;
        }
        [HttpPost]
        public async Task<IActionResult> Notify([FromBody] CreateNotificationRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Body) || string.IsNullOrWhiteSpace(request.UserId) || string.IsNullOrWhiteSpace(request.Channel))
                return BadRequest("Body, UserId, and Channel are required.");
            if (!request.Channel.Equals("email", StringComparison.OrdinalIgnoreCase))
                return BadRequest("Supported channel: email.");

            try
            {
                var result = await _NotificationService.NotifyAsync(request);
                return result ? Accepted() : Problem("Notification could not be saved.");
            }
            catch (Exception ex)
            {
                return Problem($"Notification was saved or queued unsuccessfully: {ex.Message}");
            }
        }
    }
}
