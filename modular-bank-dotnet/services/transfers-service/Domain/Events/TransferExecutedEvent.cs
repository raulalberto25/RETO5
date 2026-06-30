namespace FinBank.TransfersService.Domain.Events;

/// <summary>
/// CloudEvents 1.0 compatible event: Transfer Executed
/// Published to RabbitMQ for consumers (Accounts MS, Notifications, Audit)
/// </summary>
public record TransferExecutedEvent(
    string SpecVersion = "1.0",
    string Type = "com.finbank.transfers.executed.v1",
    string Source = "/services/transfers-service",
    string Id = "",
    DateTime Time = default,
    string DataContentType = "application/json",
    string DataSchema = "urn:com.finbank:transfers:executed:v1",
    string Subject = "",
    string CorrelationId = "",
    TransferExecutedData Data = null!
);

/// <summary>
/// Event payload: contains transfer details
/// </summary>
public record TransferExecutedData(
    Guid TransferId,
    Guid SourceAccountId,
    Guid TargetAccountId,
    Guid UserId,
    decimal Amount,
    string? Reference,
    DateTime OccurredAt
);
