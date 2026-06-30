namespace FinBank.TransfersService.Application.Dto;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// HTTP request body for POST /transfers
/// </summary>
public record TransferRequest(
    [Required] Guid SourceAccountId,
    [Required] Guid TargetAccountId,
    [Range(0.01, double.MaxValue)] decimal Amount,
    string? Reference = null
);

/// <summary>
/// HTTP response body for transfers
/// </summary>
public record TransferResponse(
    Guid Id,
    Guid SourceAccountId,
    Guid TargetAccountId,
    decimal Amount,
    string? Reference,
    DateTime CreatedAt
);
