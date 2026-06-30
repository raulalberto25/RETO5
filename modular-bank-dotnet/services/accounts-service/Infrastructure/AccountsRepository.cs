namespace FinBank.AccountsService.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Application.Ports;
using Domain;

/// <summary>
/// Adapter: implements IAccountsRepository (port).
/// Translates domain objects to/from database.
/// Infrastructure layer.
/// </summary>
public class AccountsRepository : IAccountsRepository
{
    private readonly AccountsDbContext _context;

    public AccountsRepository(AccountsDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<Account> CreateAsync(Account account, CancellationToken cancellationToken = default)
    {
        if (account == null) throw new ArgumentNullException(nameof(account));

        _context.Accounts.Add(account);
        await _context.SaveChangesAsync(cancellationToken);

        return account;
    }

    public async Task<Account?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty) throw new ArgumentException("Id cannot be empty", nameof(id));

        return await _context.Accounts.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
    }

    public async Task<List<Account>> FindByOwnerAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty) throw new ArgumentException("UserId cannot be empty", nameof(userId));

        return await _context.Accounts
            .Where(a => a.UserId == userId)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<Money> GetBalanceAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        if (accountId == Guid.Empty)
            throw new ArgumentException("AccountId cannot be empty", nameof(accountId));

        var account = await _context.Accounts.FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken)
            ?? throw new KeyNotFoundException($"Account {accountId} not found");

        return account.Balance;
    }

    public async Task DebitAsync(Guid accountId, Money amount, CancellationToken cancellationToken = default)
    {
        if (accountId == Guid.Empty)
            throw new ArgumentException("AccountId cannot be empty", nameof(accountId));
        if (amount == null) throw new ArgumentNullException(nameof(amount));

        var rowsAffected = await _context.Accounts
            .Where(a => a.Id == accountId && a.Balance.Amount >= amount.Amount)
            .ExecuteUpdateAsync(
                s => s.SetProperty(
                    a => a.Balance,
                    a => new Money(a.Balance.Amount - amount.Amount)),
                cancellationToken);

        if (rowsAffected == 0)
        {
            var account = await _context.Accounts.FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);
            if (account == null)
                throw new KeyNotFoundException($"Account {accountId} not found");

            throw new InvalidOperationException(
                $"Insufficient funds: balance {account.Balance.Amount}, attempting to debit {amount.Amount}");
        }
    }

    public async Task CreditAsync(Guid accountId, Money amount, CancellationToken cancellationToken = default)
    {
        if (accountId == Guid.Empty)
            throw new ArgumentException("AccountId cannot be empty", nameof(accountId));
        if (amount == null) throw new ArgumentNullException(nameof(amount));

        var rowsAffected = await _context.Accounts
            .Where(a => a.Id == accountId)
            .ExecuteUpdateAsync(
                s => s.SetProperty(
                    a => a.Balance,
                    a => new Money(a.Balance.Amount + amount.Amount)),
                cancellationToken);

        if (rowsAffected == 0)
            throw new KeyNotFoundException($"Account {accountId} not found");
    }
}
