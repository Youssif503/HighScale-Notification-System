using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RabbitMQ.Client;

namespace NotificationSystem.Services
{
    public interface INotificationPublisher
    {
        Task PublishAsync(string notificationId);
    }

    public class RabbitMqPublisher : INotificationPublisher
    {
        private readonly IConfiguration _configuration;

        public RabbitMqPublisher(IConfiguration configuration) => _configuration = configuration;

        public async Task PublishAsync(string NotificationId)
        {
            var factory = new ConnectionFactory
            {
                HostName = _configuration["RabbitMQ:Host"] ?? "localhost",
                UserName = _configuration["RabbitMQ:User"] ?? "guest",
                Password = _configuration["RabbitMQ:Password"] ?? "guest"
            };

            await using var _connection = await factory.CreateConnectionAsync();

            await using var _channel = await _connection.CreateChannelAsync(
                new CreateChannelOptions(
                    publisherConfirmationsEnabled: true,
                    publisherConfirmationTrackingEnabled: true
                )
            );

            await _channel.QueueDeclareAsync(
                queue: "notifications",
                durable: true,
                exclusive: false,
                autoDelete: false
            );

            var body = Encoding.UTF8.GetBytes(NotificationId.ToString());

            var properties = new BasicProperties
            {
                DeliveryMode = DeliveryModes.Persistent,
                ContentType = "text/plain"
            };

            await _channel.BasicPublishAsync(
                exchange: "",
                routingKey: "notifications",
                mandatory: true,
                basicProperties: properties,
                body: body
            );

        }
    }
}
