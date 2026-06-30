namespace FinBank.TransfersService.Infrastructure.Messaging;

using System.Text;
using RabbitMQ.Client;
using Application.Ports;

/// <summary>
/// Adapter: implements IEventPublisher using RabbitMQ
/// Publishes CloudEvents to the exchange
/// Uses connection pool for efficiency
/// </summary>
public class RabbitMqPublisher : IEventPublisher
{
    private readonly IConnectionFactory _connectionFactory;
    private readonly ILogger<RabbitMqPublisher> _logger;

    public RabbitMqPublisher(IConnectionFactory connectionFactory, ILogger<RabbitMqPublisher> logger)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task PublishAsync(string routingKey, string eventJson, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(routingKey))
            throw new ArgumentException("Routing key cannot be empty", nameof(routingKey));
        if (string.IsNullOrWhiteSpace(eventJson))
            throw new ArgumentException("Event JSON cannot be empty", nameof(eventJson));

        try
        {
            using var connection = _connectionFactory.CreateConnection();
            using var channel = connection.CreateModel();

            // Declare exchange (topic type for routing key patterns)
            channel.ExchangeDeclare(
                exchange: "banking.events",
                type: ExchangeType.Topic,
                durable: true,
                autoDelete: false);

            // Declare queues and bindings
            DeclareQueues(channel);

            // Publish message
            var body = Encoding.UTF8.GetBytes(eventJson);
            var props = channel.CreateBasicProperties();
            props.Persistent = true;
            props.ContentType = "application/json";
            props.DeliveryMode = 2;  // Persistent

            channel.BasicPublish(
                exchange: "banking.events",
                routingKey: routingKey,
                basicProperties: props,
                body: body);

            _logger.LogInformation("Published event to {Exchange} with routing key {RoutingKey}",
                "banking.events", routingKey);

            // Note: This is async-wrapped but RabbitMQ.Client is synchronous
            // In production, use async version or queue publishing to separate thread
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish event with routing key {RoutingKey}", routingKey);
            throw;
        }
    }

    private void DeclareQueues(IModel channel)
    {
        // Declare queues for consumers (durable, so messages persist)
        channel.QueueDeclare(
            queue: "notifications.transfer-executed",
            durable: true,
            exclusive: false,
            autoDelete: false);

        channel.QueueDeclare(
            queue: "audit.transfer-executed",
            durable: true,
            exclusive: false,
            autoDelete: false);

        // Bind queues to exchange
        channel.QueueBind(
            queue: "notifications.transfer-executed",
            exchange: "banking.events",
            routingKey: "transfer.executed.v*");

        channel.QueueBind(
            queue: "audit.transfer-executed",
            exchange: "banking.events",
            routingKey: "transfer.executed.v*");
    }
}
