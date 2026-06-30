namespace FinBank.TransfersService.Application.Ports;

/// <summary>
/// Output port: abstraction for calling Accounts MS
/// Adapter pattern: HttpAccountsAdapter implements this
/// </summary>
public interface IAccountsPort
{
    /// <summary>
    /// Verify account ownership and get account info
    /// </summary>
    Task<AccountInfo> FindAccountAsync(Guid accountId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Debit (withdraw) from source account
    /// </summary>
    Task DebitAsync(Guid accountId, decimal amount, CancellationToken cancellationToken = default);

    /// <summary>
    /// Credit (deposit) to target account
    /// </summary>
    Task CreditAsync(Guid accountId, decimal amount, CancellationToken cancellationToken = default);
}

public record AccountInfo(Guid Id, Guid UserId, string AccountNumber, decimal Balance);
