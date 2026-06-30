namespace FinBank.AccountsService.Api;

using System.Security.Claims;
using Application;
using Application.Dto;
using Domain;

/// <summary>
/// HTTP endpoints for accounts.
/// Input port: adapter from HTTP to application.
/// </summary>
public static class AccountsEndpoints
{
    public static void MapAccountsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/accounts")
            .RequireAuthorization();

        group.MapGet("/", GetAccounts)
            .WithName("GetAccounts")
            .WithOpenApi()
            .Produces<List<AccountSummary>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/", CreateAccount)
            .WithName("CreateAccount")
            .WithOpenApi()
            .Produces<AccountSummary>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status400BadRequest);

        group.MapGet("/{id:guid}/balance", GetBalance)
            .WithName("GetBalance")
            .WithOpenApi()
            .Produces<BalanceResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/debit", DebitAccount)
            .WithName("DebitAccount")
            .WithOpenApi()
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{id:guid}/credit", CreditAccount)
            .WithName("CreditAccount")
            .WithOpenApi()
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> GetAccounts(
        ClaimsPrincipal user,
        AccountsUseCase useCase,
        CancellationToken cancellationToken)
    {
        try
        {
            var userIdStr = user.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? user.FindFirstValue("sub");

            if (userIdStr == null || !Guid.TryParse(userIdStr, out var userId))
                return Results.Unauthorized();

            var accounts = await useCase.FindByOwnerAsync(userId, cancellationToken);
            return Results.Ok(accounts);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> CreateAccount(
        ClaimsPrincipal user,
        AccountsUseCase useCase,
        CancellationToken cancellationToken)
    {
        try
        {
            var userIdStr = user.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? user.FindFirstValue("sub");

            if (userIdStr == null || !Guid.TryParse(userIdStr, out var userId))
                return Results.Unauthorized();

            var account = await useCase.CreateAccountAsync(userId, cancellationToken);
            return Results.Created($"/accounts/{account.Id}", account);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> GetBalance(
        Guid id,
        ClaimsPrincipal user,
        AccountsUseCase useCase,
        CancellationToken cancellationToken)
    {
        try
        {
            var userIdStr = user.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? user.FindFirstValue("sub");

            if (userIdStr == null || !Guid.TryParse(userIdStr, out var userId))
                return Results.Unauthorized();

            // Verify ownership
            var account = await useCase.FindByIdAsync(id, cancellationToken);
            if (account == null)
                return Results.NotFound();

            if (!userId.ToString().Equals(account.Id.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                // Need to check if this account belongs to the user
                var userAccounts = await useCase.FindByOwnerAsync(userId, cancellationToken);
                if (!userAccounts.Any(a => a.Id == id))
                    return Results.Forbid();
            }

            var balance = await useCase.GetBalanceAsync(id, cancellationToken);
            return Results.Ok(new BalanceResponse { Amount = balance.Amount.ToString("F4") });
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> DebitAccount(
        Guid id,
        DebitCreditRequest request,
        ClaimsPrincipal user,
        AccountsUseCase useCase,
        CancellationToken cancellationToken)
    {
        try
        {
            if (request == null || request.Amount <= 0)
                return Results.BadRequest(new { error = "Amount must be greater than 0" });

            await useCase.DebitAsync(id, Money.Of(request.Amount), request.Reference, cancellationToken);
            return Results.NoContent();
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Results.UnprocessableEntity(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> CreditAccount(
        Guid id,
        DebitCreditRequest request,
        ClaimsPrincipal user,
        AccountsUseCase useCase,
        CancellationToken cancellationToken)
    {
        try
        {
            if (request == null || request.Amount <= 0)
                return Results.BadRequest(new { error = "Amount must be greater than 0" });

            await useCase.CreditAsync(id, Money.Of(request.Amount), request.Reference, cancellationToken);
            return Results.NoContent();
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    public record BalanceResponse
    {
        public string Amount { get; set; } = "";
    }

    public record DebitCreditRequest
    {
        public decimal Amount { get; set; }
        public string? Reference { get; set; }
    }
}
