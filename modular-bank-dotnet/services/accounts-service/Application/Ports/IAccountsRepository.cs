namespace FinBank.AccountsService.Application.Ports;

using Domain;

/// <summary>
/// Output port: abstraction for account persistence.
/// Application layer depends on this; Infrastructure implements it.
/// </summary>
public interface IAccountsRepository
{
    Task<Account> CreateAsync(Account account, CancellationToken cancellationToken = default);
    Task<Account?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<Account>> FindByOwnerAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<Money> GetBalanceAsync(Guid accountId, CancellationToken cancellationToken = default);
    Task DebitAsync(Guid accountId, Money amount, CancellationToken cancellationToken = default);
    Task CreditAsync(Guid accountId, Money amount, CancellationToken cancellationToken = default);
}
