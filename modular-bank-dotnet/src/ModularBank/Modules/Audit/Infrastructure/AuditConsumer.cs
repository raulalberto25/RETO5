using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;
using ModularBank.Modules.Audit.Application;

namespace ModularBank.Modules.Audit.Infrastructure;

/// <summary>
/// Background service: consumes transfer.executed events from RabbitMQ
/// Records audit entries when transfers complete
/// </summary>
public class AuditConsumer : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConnectionFactory _connectionFactory;
    private readonly ILogger<AuditConsumer> _logger;
    private IConnection? _connection;
    private IModel? _channel;

    public AuditConsumer(
        IServiceProvider serviceProvider,
        IConnectionFactory connectionFactory,
        ILogger<AuditConsumer> logger)
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
                queue: "audit.transfer-executed",
                durable: true,
                exclusive: false,
                autoDelete: false);

            _channel.QueueBind(
                queue: "audit.transfer-executed",
                exchange: "banking.events",
                routingKey: "transfer.executed.v*");

            // Set up consumer
            var consumer = new AsyncEventingBasicConsumer(_channel);
            consumer.Received += async (model, ea) => await HandleMessageAsync(ea, stoppingToken);

            _channel.BasicConsume(
                queue: "audit.transfer-executed",
                autoAck: false,
                consumerTag: "audit-consumer",
                consumer: consumer);

            _logger.LogInformation("AuditConsumer started, listening to transfer.executed events");

            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(1000, stoppingToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in AuditConsumer");
        }
        finally
        {
            _channel?.Dispose();
            _connection?.Dispose();
            _logger.LogInformation("AuditConsumer stopped");
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

            _logger.LogInformation("Received audit event: {Message}", message);

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

            // Record audit entry
            using var scope = _serviceProvider.CreateScope();
            var auditService = scope.ServiceProvider.GetRequiredService<IAuditService>();

            var metadata = new Dictionary<string, string>
            {
                { "transferId", data.Data.TransferId.ToString() },
                { "amount", data.Data.Amount.ToString() },
                { "sourceAccountId", data.Data.SourceAccountId.ToString() },
                { "targetAccountId", data.Data.TargetAccountId.ToString() }
            };

            await auditService.RecordAsync(
                data.Data.UserId,
                "TRANSFER_EXECUTED",
                metadata);

            // Acknowledge message
            _channel?.BasicAck(ea.DeliveryTag, false);

            _logger.LogInformation("Audit entry recorded for transfer {TransferId}", data.Data.TransferId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing audit event");
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
