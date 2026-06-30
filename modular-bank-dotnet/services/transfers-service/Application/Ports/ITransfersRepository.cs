namespace FinBank.TransfersService.Application.Ports;

using Domain;

/// <summary>
/// Output port: abstraction for transfer persistence
/// Adapter: TransfersRepository implements this
/// </summary>
public interface ITransfersRepository
{
    /// <summary>
    /// Save transfer + outbox entry in single transaction
    /// Guarantees atomicity: both save or both fail
    /// </summary>
    Task SaveTransferWithOutboxAsync(Transfer transfer, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get transfer history for account (as source or target)
    /// </summary>
    Task<List<Transfer>> GetTransferHistoryAsync(Guid accountId, CancellationToken cancellationToken = default);
}
