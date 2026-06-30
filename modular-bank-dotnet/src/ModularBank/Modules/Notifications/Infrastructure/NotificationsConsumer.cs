using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;
using ModularBank.Modules.Notifications.Application;

namespace ModularBank.Modules.Notifications.Infrastructure;

/// <summary>
/// Background service: consumes transfer.executed events from RabbitMQ
/// Creates notification records when transfers complete
/// </summary>
public class NotificationsConsumer : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConnectionFactory _connectionFactory;
    private readonly ILogger<NotificationsConsumer> _logger;
    private IConnection? _connection;
    private IModel? _channel;

    public NotificationsConsumer(
        IServiceProvider serviceProvider,
        IConnectionFactory connectionFactory,
        ILogger<NotificationsConsumer> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _connection = _connectionFactory.CreateConnection();
            _channel = _connection.CreateModel();

            // Declare exchange and queue
            _channel.ExchangeDeclare(
                exchange: "banking.events",
                type: ExchangeType.Topic,
                durable: true,
                autoDelete: false);

            _channel.QueueDeclare(
                queue: "notifications.transfer-executed",
                durable: true,
                exclusive: false,
                autoDelete: false);

            _channel.QueueBind(
                queue: "notifications.transfer-executed",
                exchange: "banking.events",
                routingKey: "transfer.executed.v*");

            // Set up consumer
            var consumer = new AsyncEventingBasicConsumer(_channel);
            consumer.Received += async (model, ea) => await HandleMessageAsync(ea, stoppingToken);

            _channel.BasicConsume(
                queue: "notifications.transfer-executed",
                autoAck: false,
                consumerTag: "notifications-consumer",
                consumer: consumer);

            _logger.LogInformation("NotificationsConsumer started, listening to transfer.executed events");

            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(1000, stoppingToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in NotificationsConsumer");
        }
        finally
        {
            _channel?.Dispose();
            _connection?.Dispose();
            _logger.LogInformation("NotificationsConsumer stopped");
        }
    }

    private async Task HandleMessageAsync(BasicDeliverEventArgs ea, CancellationToken stoppingToken)
    {
        try
        {
            var message = Encoding.UTF8.GetString(ea.Body.ToArray());

            // Parse CloudEvent
            var @event = JsonSerializer.Deserialize<dynamic>(message,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (@event == null)
            {
                _logger.LogWarning("Received null event");
                _channel?.BasicNack(ea.DeliveryTag, false, false);
                return;
            }

            _logger.LogInformation("Received event: {Message}", message);

            // Extract data from CloudEvent payload
            var data = JsonSerializer.Deserialize<TransferEventData>(
                message,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (data?.Data == null)
            {
                _logger.LogWarning("Event data is null");
                _channel?.BasicAck(ea.DeliveryTag, false);
                return;
            }

            // Create notification
            using var scope = _serviceProvider.CreateScope();
            var notificationsService = scope.ServiceProvider.GetRequiredService<INotificationsService>();

            var payload = new Dictionary<string, string>
            {
                { "amount", data.Data.Amount.ToString() },
                { "targetAccountId", data.Data.TargetAccountId.ToString() },
                { "transferId", data.Data.TransferId.ToString() }
            };

            await notificationsService.SendAsync(
                data.Data.UserId,
                NotificationType.TransferSent,
                payload);

            // Acknowledge message
            _channel?.BasicAck(ea.DeliveryTag, false);

            _logger.LogInformation("Notification created for user {UserId}", data.Data.UserId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing notification event");
            // Nack and requeue
            _channel?.BasicNack(ea.DeliveryTag, false, true);
        }
    }

    private record TransferEventData(
        string SpecVersion,
        string Type,
        string Source,
        string Id,
        DateTime Time,
        string DataContentType,
        string Subject,
        string CorrelationId,
        TransferData Data
    );

    private record TransferData(
        Guid TransferId,
        Guid SourceAccountId,
        Guid TargetAccountId,
        Guid UserId,
        decimal Amount,
        string? Reference,
        DateTime OccurredAt
    );
}
