namespace FinBank.TransfersService.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Application.Ports;
using Domain;

/// <summary>
/// Adapter: implements ITransfersRepository using EF Core
/// Handles atomic save of transfer + outbox entry
/// </summary>
public class TransfersRepository : ITransfersRepository
{
    private readonly TransfersDbContext _context;
    private readonly ILogger<TransfersRepository> _logger;

    public TransfersRepository(TransfersDbContext context, ILogger<TransfersRepository> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task SaveTransferWithOutboxAsync(Transfer transfer, CancellationToken cancellationToken = default)
    {
        if (transfer == null) throw new ArgumentNullException(nameof(transfer));

        using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            // Step 1: Save transfer
            _context.Transfers.Add(transfer);
            await _context.SaveChangesAsync(cancellationToken);

            // Step 2: Create outbox entry (event will be published asynchronously)
            var outboxEntry = new OutboxEntry
            {
                Id = Guid.NewGuid(),
                AggregateId = transfer.Id,
                EventType = "transfer.executed",
                RoutingKey = "transfer.executed.v1",
                Payload = System.Text.Json.JsonSerializer.Serialize(new
                {
                    transferId = transfer.Id,
                    sourceAccountId = transfer.SourceAccountId,
                    targetAccountId = transfer.TargetAccountId,
                    userId = transfer.UserId,
                    amount = transfer.Amount,
                    reference = transfer.Reference,
                    occurredAt = transfer.CreatedAt
                }),
                PublishedAt = null  // Not published yet
            };

            _context.OutboxEntries.Add(outboxEntry);
            await _context.SaveChangesAsync(cancellationToken);

            // Commit both save or rollback both
            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation("Transfer {TransferId} saved with outbox entry", transfer.Id);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Failed to save transfer {TransferId}", transfer.Id);
            throw;
        }
    }

    public async Task<List<Transfer>> GetTransferHistoryAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        if (accountId == Guid.Empty)
            throw new ArgumentException("AccountId cannot be empty", nameof(accountId));

        return await _context.Transfers
            .Where(t => t.SourceAccountId == accountId || t.TargetAccountId == accountId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(cancellationToken);
    }
}
