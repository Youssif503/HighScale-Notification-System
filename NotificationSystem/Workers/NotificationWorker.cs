
using System.Text;
using Microsoft.EntityFrameworkCore;
using NotificationSystem.Data;
using NotificationSystem.Models;
using NotificationSystem.Services;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace NotificationSystem.Workers
{
    public class NotificationWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<NotificationWorker> _logger;
        public NotificationWorker(IServiceScopeFactory scopeFactory,
        ILogger<NotificationWorker> logger,
        IConfiguration configuration)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _configuration = configuration;
        }

        protected async override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var factory = new ConnectionFactory
            {
                HostName = _configuration["RabbitMQ:Host"] ?? "localhost",
                UserName = _configuration["RabbitMQ:User"] ?? "guest",
                Password = _configuration["RabbitMQ:Password"] ?? "guest"
            };

            await using var _connection = await factory.CreateConnectionAsync(stoppingToken);

            await using var _channel = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);

            await _channel.QueueDeclareAsync(
                    queue: "notifications",
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    cancellationToken: stoppingToken
            );

            await _channel.BasicQosAsync(
                prefetchSize: 0,
                prefetchCount: 1,
                global: false,
                cancellationToken: stoppingToken
            );

            var consumer = new AsyncEventingBasicConsumer(_channel);

            consumer.ReceivedAsync += async (_, ea) =>
            {
                var message = Encoding.UTF8.GetString(ea.Body.ToArray());
                var owner = Guid.NewGuid().ToString("N");
                var claimAcquired = false;

                try
                {
                    string NotificationId = message;

                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var idempotency = scope.ServiceProvider.GetRequiredService<NotificationIdempotencyStore>();
                    var claim = await idempotency.TryClaimAsync(message, owner);
                    if (claim != IdempotencyClaimResult.Acquired)
                    {
                        _logger.LogInformation("Skipping notification {Id}; Redis idempotency status is {Status}", message, claim);
                        if (claim == IdempotencyClaimResult.AlreadyCompleted)
                            await _channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
                        else
                            await _channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true);
                        return;
                    }
                    claimAcquired = true;

                    var notification = await db.Notifications.Include(x => x.User)
                        .FirstOrDefaultAsync(x => x.Id == message, stoppingToken);
                    if (notification is null)
                    {
                        _logger.LogInformation("Notification {id} not Found", NotificationId);
                        await idempotency.CompleteAsync(message, owner);
                        await _channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
                        return;
                    }

                    if (notification.status == NotificationStatus.send.ToString())
                    {
                        await idempotency.CompleteAsync(message, owner);
                        await _channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
                        return;
                    }

                    var sender = notification.channel.Equals("email", StringComparison.OrdinalIgnoreCase)
                        ? scope.ServiceProvider.GetRequiredService<INotificationProvider>()
                        : throw new NotSupportedException($"Notification channel '{notification.channel}' is not supported.");

                    var result = await sender.SendAsync(notification, stoppingToken);
                    if (!result)
                        throw new InvalidOperationException($"Provider reported notification {message} was not sent.");

                    notification.status = NotificationStatus.send.ToString();
                    await db.SaveChangesAsync(stoppingToken);
                    await idempotency.CompleteAsync(message, owner);

                    await _channel.BasicAckAsync(
                        ea.DeliveryTag,
                        multiple: false);

                    _logger.LogInformation(
                        "Notification {Id} sent successfully",
                        NotificationId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Failed to process notification {Message}",
                        message);

                    // Release our claim so RabbitMQ redelivery can retry the send.
                    try
                    {
                        if (claimAcquired)
                        {
                            using var scope = _scopeFactory.CreateScope();
                            var idempotency = scope.ServiceProvider.GetRequiredService<NotificationIdempotencyStore>();
                            await idempotency.ReleaseAsync(message, owner);
                        }
                    }
                    catch (Exception releaseException)
                    {
                        _logger.LogError(releaseException, "Could not release idempotency claim for {Id}", message);
                    }
                    await _channel.BasicNackAsync(
                        ea.DeliveryTag,
                        multiple: false,
                        requeue: true);
                }
            };

            await _channel.BasicConsumeAsync(
            queue: "notifications",
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

            _logger.LogInformation(
                "Notification worker is listening to RabbitMQ");

            await Task.Delay(
                Timeout.Infinite,
                stoppingToken);
        }
    }
}
