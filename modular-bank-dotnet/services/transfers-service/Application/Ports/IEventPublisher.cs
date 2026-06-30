namespace FinBank.TransfersService.Application.Ports;

/// <summary>
/// Output port: abstraction for publishing events to broker
/// Adapter: RabbitMqPublisher implements this
/// Guarantees at-least-once delivery via Outbox pattern
/// </summary>
public interface IEventPublisher
{
    /// <summary>
    /// Publish event to message broker
    /// (Actual publish happens asynchronously via OutboxWorker)
    /// </summary>
    Task PublishAsync(string routingKey, string eventJson, CancellationToken cancellationToken = default);
}
