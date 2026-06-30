namespace FinBank.TransfersService.Infrastructure.Messaging;

using System.Text;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;

/// <summary>
/// Background service: processes outbox entries
/// Polls database for unpublished events, publishes to RabbitMQ
/// Guarantees at-least-once delivery: only marks published after success
/// </summary>
public class OutboxWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConnectionFactory _connectionFactory;
    private readonly ILogger<OutboxWorker> _logger;
    private readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(5);

    public OutboxWorker(
        IServiceProvider serviceProvider,
        IConnectionFactory connectionFactory,
        ILogger<OutboxWorker> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OutboxWorker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOutboxEntriesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing outbox entries");
            }

            await Task.Delay(_pollInterval, stoppingToken);
        }

        _logger.LogInformation("OutboxWorker stopped");
    }

    private async Task ProcessOutboxEntriesAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TransfersDbContext>();

        // Find unpublished entries
        var unpublished = await dbContext.OutboxEntries
            .Where(e => e.PublishedAt == null)
            .OrderBy(e => e.CreatedAt)
            .Take(100)  // Process in batches
            .ToListAsync(stoppingToken);

        if (unpublished.Count == 0)
            return;

        _logger.LogInformation("Processing {Count} outbox entries", unpublished.Count);

        foreach (var entry in unpublished)
        {
            try
            {
                // Publish to RabbitMQ
                await PublishEventAsync(entry, stoppingToken);

                // Mark as published
                entry.PublishedAt = DateTime.UtcNow;
                dbContext.Update(entry);
                await dbContext.SaveChangesAsync(stoppingToken);

                _logger.LogInformation("Published outbox entry {Id} for aggregate {AggregateId}",
                    entry.Id, entry.AggregateId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish outbox entry {Id}, will retry later", entry.Id);
                // Don't mark as published, will retry in next poll
            }
        }
    }

    private async Task PublishEventAsync(OutboxEntry entry, CancellationToken stoppingToken)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            using var channel = connection.CreateModel();

            // Declare exchange
            channel.ExchangeDeclare(
                exchange: "banking.events",
                type: ExchangeType.Topic,
                durable: true,
                autoDelete: false);

            // Declare queues
            DeclareQueues(channel);

            // Publish
            var body = Encoding.UTF8.GetBytes(entry.Payload);
            var props = channel.CreateBasicProperties();
            props.Persistent = true;
            props.ContentType = "application/json";
            props.DeliveryMode = 2;

            channel.BasicPublish(
                exchange: "banking.events",
                routingKey: entry.RoutingKey ?? "transfer.executed.v1",
                basicProperties: props,
                body: body);

            await Task.CompletedTask;
        }
        catch (Exception)
        {
            throw;
        }
    }

    private void DeclareQueues(IModel channel)
    {
        channel.QueueDeclare("notifications.transfer-executed", true, false, false);
        channel.QueueDeclare("audit.transfer-executed", true, false, false);
        channel.QueueBind("notifications.transfer-executed", "banking.events", "transfer.executed.v*");
        channel.QueueBind("audit.transfer-executed", "banking.events", "transfer.executed.v*");
    }
}
