namespace FinBank.TransfersService.Domain;

/// <summary>
/// Transfer aggregate root - pure domain logic.
/// Represents a completed transfer between two accounts.
/// </summary>
public class Transfer
{
    public Guid Id { get; set; }
    public Guid SourceAccountId { get; set; }
    public Guid TargetAccountId { get; set; }
    public Guid UserId { get; set; }
    public decimal Amount { get; set; }
    public string? Reference { get; set; }
    public DateTime CreatedAt { get; set; }

    public Transfer() { }

    public Transfer(
        Guid userId,
        Guid sourceAccountId,
        Guid targetAccountId,
        decimal amount,
        string? reference = null)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("UserId cannot be empty", nameof(userId));
        if (sourceAccountId == Guid.Empty)
            throw new ArgumentException("SourceAccountId cannot be empty", nameof(sourceAccountId));
        if (targetAccountId == Guid.Empty)
            throw new ArgumentException("TargetAccountId cannot be empty", nameof(targetAccountId));
        if (amount <= 0)
            throw new ArgumentException("Amount must be greater than 0", nameof(amount));
        if (sourceAccountId == targetAccountId)
            throw new ArgumentException("Cannot transfer to the same account", nameof(targetAccountId));

        Id = Guid.NewGuid();
        UserId = userId;
        SourceAccountId = sourceAccountId;
        TargetAccountId = targetAccountId;
        Amount = amount;
        Reference = reference;
        CreatedAt = DateTime.UtcNow;
    }
}
