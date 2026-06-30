namespace FinBank.AccountsService.Application.Dto;

/// <summary>
/// Data transfer object for account responses.
/// Maps domain Account to HTTP response.
/// </summary>
public record AccountSummary(
    Guid Id,
    string AccountNumber,
    decimal Balance
);
