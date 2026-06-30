namespace FinBank.AccountsService.Domain;

/// <summary>
/// Money value object - represents an amount of currency.
/// Domain logic: no external dependencies.
/// </summary>
public record Money
{
    public decimal Amount { get; }

    public Money(decimal amount)
    {
        if (amount < 0)
            throw new ArgumentException("Amount must be non-negative", nameof(amount));

        Amount = amount;
    }

    public static Money Of(decimal amount) => new(amount);
    public static Money Zero => new(0);

    public Money Add(Money other)
    {
        if (other == null) throw new ArgumentNullException(nameof(other));
        return new(Amount + other.Amount);
    }

    public Money Subtract(Money other)
    {
        if (other == null) throw new ArgumentNullException(nameof(other));
        if (other.Amount > Amount)
            throw new InvalidOperationException($"Insufficient funds: cannot subtract {other.Amount} from {Amount}");
        return new(Amount - other.Amount);
    }

    public bool IsGreaterThanOrEqualTo(Money other)
    {
        if (other == null) throw new ArgumentNullException(nameof(other));
        return Amount >= other.Amount;
    }

    public override string ToString() => Amount.ToString("F4");
}
