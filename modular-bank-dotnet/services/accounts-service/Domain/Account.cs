namespace FinBank.AccountsService.Domain;

/// <summary>
/// Account aggregate root - pure domain logic, no infrastructure.
/// </summary>
public class Account
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string AccountNumber { get; set; } = null!;
    public Money Balance { get; set; } = Money.Zero;
    public DateTime CreatedAt { get; set; }

    public Account() { }

    public Account(Guid userId, string accountNumber)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("UserId cannot be empty", nameof(userId));
        if (string.IsNullOrWhiteSpace(accountNumber))
            throw new ArgumentException("AccountNumber cannot be empty", nameof(accountNumber));

        Id = Guid.NewGuid();
        UserId = userId;
        AccountNumber = accountNumber;
        Balance = Money.Zero;
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Debit (withdraw) money from account.
    /// </summary>
    public void Debit(Money amount)
    {
        if (amount == null) throw new ArgumentNullException(nameof(amount));
        Balance = Balance.Subtract(amount);
    }

    /// <summary>
    /// Credit (deposit) money to account.
    /// </summary>
    public void Credit(Money amount)
    {
        if (amount == null) throw new ArgumentNullException(nameof(amount));
        Balance = Balance.Add(amount);
    }

    public bool IsOwnedBy(Guid userId) => UserId == userId;
}
