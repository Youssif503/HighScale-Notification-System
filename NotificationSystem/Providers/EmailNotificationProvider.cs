using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using NotificationSystem.Models;
using NotificationSystem.Workers;

namespace NotificationSystem.Providers
{
    public class EmailNotificationProvider : INotificationProvider
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<EmailNotificationProvider> _logger;

        public EmailNotificationProvider(IConfiguration configuration, ILogger<EmailNotificationProvider> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<bool> SendAsync(Notification notification, CancellationToken cancellationToken)
        {
            var email = notification.User?.Email;
            if (string.IsNullOrWhiteSpace(email))
                throw new InvalidOperationException($"Notification {notification.Id} has no recipient email address.");

            var host = _configuration["Email:Smtp:Host"];
            var fromAddress = _configuration["Email:FromAddress"];
            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(fromAddress))
                throw new InvalidOperationException("Email:Smtp:Host and Email:FromAddress must be configured.");

            using var message = new MailMessage
            {
                From = new MailAddress(fromAddress, _configuration["Email:FromName"] ?? "Notification System"),
                Subject = _configuration["Email:Subject"] ?? "Notification",
                Body = notification.body,
                IsBodyHtml = false
            };
            message.To.Add(new MailAddress(email));
            var port = _configuration.GetValue<int?>("Email:Smtp:Port") ?? 587;
            using var client = new SmtpClient(host, port)
            {
                EnableSsl = _configuration.GetValue<bool>("Email:Smtp:UseSsl"),
                DeliveryMethod = SmtpDeliveryMethod.Network
            };
            var username = _configuration["Email:Smtp:Username"];
            var password = _configuration["Email:Smtp:Password"];
            if (!string.IsNullOrWhiteSpace(username))
                client.Credentials = new NetworkCredential(username, password ?? string.Empty);
            cancellationToken.ThrowIfCancellationRequested();
            await client.SendMailAsync(message, cancellationToken);
            _logger.LogInformation("Email sent for notification {NotificationId} to {Recipient}", notification.Id, email);
            return true;
        }
    }
}
