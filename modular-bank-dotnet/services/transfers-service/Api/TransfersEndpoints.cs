namespace FinBank.TransfersService.Api;

using System.Security.Claims;
using Application;
using Application.Dto;

/// <summary>
/// HTTP endpoints for transfers
/// Input port: adapter from HTTP to application
/// </summary>
public static class TransfersEndpoints
{
    public static void MapTransfersEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/transfers")
            .RequireAuthorization();

        group.MapPost("/", ExecuteTransfer)
            .WithName("ExecuteTransfer")
            .WithOpenApi()
            .Produces<TransferResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("", GetTransferHistory)
            .WithName("GetTransferHistory")
            .WithOpenApi()
            .Produces<List<TransferResponse>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> ExecuteTransfer(
        TransferRequest request,
        ClaimsPrincipal user,
        TransferUseCase useCase,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!TryGetUserId(user, out var userId))
                return Results.Unauthorized();

            if (request == null)
                return Results.BadRequest(new { error = "Request body is required" });

            // Validate request
            if (request.SourceAccountId == Guid.Empty || request.TargetAccountId == Guid.Empty)
                return Results.BadRequest(new { error = "Account IDs cannot be empty" });

            if (request.Amount <= 0)
                return Results.BadRequest(new { error = "Amount must be greater than 0" });

            // Execute transfer (saga choreography)
            var transfer = await useCase.ExecuteAsync(userId, request, cancellationToken);

            logger.LogInformation("Transfer executed: {TransferId} from {Source} to {Target}",
                transfer.Id, transfer.SourceAccountId, transfer.TargetAccountId);

            return Results.Created($"/transfers/{transfer.Id}", transfer);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Results.Forbid();
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Results.UnprocessableEntity(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error executing transfer");
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<IResult> GetTransferHistory(
        [FromQuery] Guid accountId,
        ClaimsPrincipal user,
        TransferUseCase useCase,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!TryGetUserId(user, out var userId))
                return Results.Unauthorized();

            if (accountId == Guid.Empty)
                return Results.BadRequest(new { error = "accountId query parameter is required" });

            var transfers = await useCase.GetHistoryAsync(userId, accountId, cancellationToken);
            return Results.Ok(transfers);
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Forbid();
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

    private static bool TryGetUserId(ClaimsPrincipal user, out Guid userId)
    {
        var raw = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return Guid.TryParse(raw, out userId);
    }
}
