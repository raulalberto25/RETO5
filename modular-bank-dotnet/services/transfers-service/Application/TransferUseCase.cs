namespace FinBank.TransfersService.Application;

using Ports;
using Dto;
using Domain;
using Domain.Events;
using System.Text.Json;

/// <summary>
/// Transfers orchestrator: implements saga choreography pattern
/// 1. Validates ownership with Accounts MS
/// 2. Records transfer + outbox entry (single transaction)
/// 3. OutboxWorker publishes event asynchronously
/// 4. Consumers (Accounts, Notifications, Audit) react to event
/// </summary>
public class TransferUseCase
{
    private readonly IAccountsPort _accountsPort;
    private readonly IEventPublisher _eventPublisher;
    private readonly ITransfersRepository _repository;
    private readonly ILogger<TransferUseCase> _logger;

    public TransferUseCase(
        IAccountsPort accountsPort,
        IEventPublisher eventPublisher,
        ITransfersRepository repository,
        ILogger<TransferUseCase> logger)
    {
        _accountsPort = accountsPort ?? throw new ArgumentNullException(nameof(accountsPort));
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<TransferResponse> ExecuteAsync(
        Guid userId,
        TransferRequest request,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("UserId cannot be empty", nameof(userId));
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        // Step 1: Verify source account ownership
        var sourceAccount = await _accountsPort.FindAccountAsync(request.SourceAccountId, cancellationToken);
        if (sourceAccount.UserId != userId)
            throw new UnauthorizedAccessException($"Account {request.SourceAccountId} is not owned by user {userId}");

        // Step 2: Verify target account exists (no ownership check for deposits)
        var targetAccount = await _accountsPort.FindAccountAsync(request.TargetAccountId, cancellationToken);

        // Step 3: Create transfer domain object
        var transfer = new Transfer(
            userId,
            request.SourceAccountId,
            request.TargetAccountId,
            request.Amount,
            request.Reference);

        // Step 4: Save transfer + outbox entry in single transaction
        await _repository.SaveTransferWithOutboxAsync(transfer, cancellationToken);

        _logger.LogInformation("Transfer recorded: {TransferId} from {Source} to {Target} amount {Amount}",
            transfer.Id, transfer.SourceAccountId, transfer.TargetAccountId, transfer.Amount);

        // Step 5: Trigger async event publishing (OutboxWorker will handle)
        // This returns immediately; actual publish happens in background
        var evt = new TransferExecutedEvent(
            Id: transfer.Id.ToString(),
            Time: transfer.CreatedAt,
            Subject: $"transfer/{transfer.Id}",
            CorrelationId: userId.ToString(),
            Data: new(
                TransferId: transfer.Id,
                SourceAccountId: transfer.SourceAccountId,
                TargetAccountId: transfer.TargetAccountId,
                UserId: userId,
                Amount: transfer.Amount,
                Reference: transfer.Reference,
                OccurredAt: transfer.CreatedAt));

        var eventJson = JsonSerializer.Serialize(evt);
        await _eventPublisher.PublishAsync("transfer.executed.v1", eventJson, cancellationToken);

        return new TransferResponse(
            transfer.Id,
            transfer.SourceAccountId,
            transfer.TargetAccountId,
            transfer.Amount,
            transfer.Reference,
            transfer.CreatedAt);
    }

    public async Task<List<TransferResponse>> GetHistoryAsync(
        Guid userId,
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("UserId cannot be empty", nameof(userId));
        if (accountId == Guid.Empty)
            throw new ArgumentException("AccountId cannot be empty", nameof(accountId));

        // Verify account ownership
        var account = await _accountsPort.FindAccountAsync(accountId, cancellationToken);
        if (account.UserId != userId)
            throw new UnauthorizedAccessException($"Account {accountId} is not owned by user {userId}");

        // Get transfer history
        var transfers = await _repository.GetTransferHistoryAsync(accountId, cancellationToken);

        return transfers
            .Select(t => new TransferResponse(t.Id, t.SourceAccountId, t.TargetAccountId, t.Amount, t.Reference, t.CreatedAt))
            .ToList();
    }
}
