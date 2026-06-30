namespace FinBank.AccountsService.Application;

using Ports;
using Dto;
using Domain;

/// <summary>
/// Application orchestrator: business workflows.
/// Depends on ports (abstractions), not implementations.
/// No infrastructure concerns here.
/// </summary>
public class AccountsUseCase
{
    private readonly IAccountsRepository _repository;

    public AccountsUseCase(IAccountsRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<AccountSummary> CreateAccountAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("UserId cannot be empty", nameof(userId));

        // Generate account number
        var accountNumber = $"ACC{Guid.NewGuid().ToString().Substring(0, 12).ToUpperInvariant()}";

        var account = new Account(userId, accountNumber);
        var created = await _repository.CreateAsync(account, cancellationToken);

        return new AccountSummary(created.Id, created.AccountNumber, created.Balance.Amount);
    }

    public async Task<Money> GetBalanceAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        if (accountId == Guid.Empty)
            throw new ArgumentException("AccountId cannot be empty", nameof(accountId));

        return await _repository.GetBalanceAsync(accountId, cancellationToken);
    }

    public async Task DebitAsync(
        Guid accountId,
        Money amount,
        string? reference = null,
        CancellationToken cancellationToken = default)
    {
        if (accountId == Guid.Empty)
            throw new ArgumentException("AccountId cannot be empty", nameof(accountId));
        if (amount == null)
            throw new ArgumentNullException(nameof(amount));

        await _repository.DebitAsync(accountId, amount, cancellationToken);
    }

    public async Task CreditAsync(
        Guid accountId,
        Money amount,
        string? reference = null,
        CancellationToken cancellationToken = default)
    {
        if (accountId == Guid.Empty)
            throw new ArgumentException("AccountId cannot be empty", nameof(accountId));
        if (amount == null)
            throw new ArgumentNullException(nameof(amount));

        await _repository.CreditAsync(accountId, amount, cancellationToken);
    }

    public async Task<List<AccountSummary>> FindByOwnerAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("UserId cannot be empty", nameof(userId));

        var accounts = await _repository.FindByOwnerAsync(userId, cancellationToken);
        return accounts
            .Select(a => new AccountSummary(a.Id, a.AccountNumber, a.Balance.Amount))
            .ToList();
    }

    public async Task<AccountSummary?> FindByIdAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        if (accountId == Guid.Empty)
            throw new ArgumentException("AccountId cannot be empty", nameof(accountId));

        var account = await _repository.FindByIdAsync(accountId, cancellationToken);
        if (account == null) return null;

        return new AccountSummary(account.Id, account.AccountNumber, account.Balance.Amount);
    }
}
