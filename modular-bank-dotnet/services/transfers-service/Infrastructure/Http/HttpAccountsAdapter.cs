namespace FinBank.TransfersService.Infrastructure.Http;

using System.Net;
using System.Text.Json;
using Polly;
using Application.Ports;
using Resilience;

/// <summary>
/// Adapter: implements IAccountsPort via HTTP calls to Accounts MS
/// Includes resilience: Polly circuit breaker and retry
/// </summary>
public class HttpAccountsAdapter : IAccountsPort
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<HttpAccountsAdapter> _logger;
    private readonly IAsyncPolicy<HttpResponseMessage> _resiliencePolicy;

    public HttpAccountsAdapter(HttpClient httpClient, ILogger<HttpAccountsAdapter> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _resiliencePolicy = ResiliencePolicies.GetCombinedPolicy();
    }

    public async Task<AccountInfo> FindAccountAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        try
        {
            var context = new Polly.Context { { "Logger", _logger } };

            var response = await _resiliencePolicy.ExecuteAsync(
                async (ctx, ct) =>
                {
                    var req = new HttpRequestMessage(HttpMethod.Get, $"/{accountId}");
                    return await _httpClient.SendAsync(req, ct);
                },
                context,
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new KeyNotFoundException($"Account {accountId} not found in Accounts MS");

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var account = JsonSerializer.Deserialize<AccountInfo>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidOperationException("Failed to deserialize account");

            _logger.LogInformation("Found account {AccountId} via HTTP (with resilience)", accountId);
            return account;
        }
        catch (BrokenCircuitException ex)
        {
            _logger.LogError(ex, "Circuit breaker is open for Accounts MS");
            throw new InvalidOperationException("Accounts MS temporarily unavailable (circuit breaker open)", ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP request failed to find account {AccountId}", accountId);
            throw new InvalidOperationException($"Failed to find account {accountId} from Accounts MS", ex);
        }
    }

    public async Task DebitAsync(Guid accountId, decimal amount, CancellationToken cancellationToken = default)
    {
        try
        {
            var context = new Polly.Context { { "Logger", _logger } };

            var response = await _resiliencePolicy.ExecuteAsync(
                async (ctx, ct) =>
                {
                    var request = new HttpRequestMessage(HttpMethod.Post, $"/{accountId}/debit")
                    {
                        Content = new StringContent(
                            JsonSerializer.Serialize(new { amount }),
                            System.Text.Encoding.UTF8,
                            "application/json")
                    };
                    return await _httpClient.SendAsync(request, ct);
                },
                context,
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new KeyNotFoundException($"Account {accountId} not found");

            if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException($"Insufficient funds: {errorContent}");
            }

            response.EnsureSuccessStatusCode();
            _logger.LogInformation("Debit successful for account {AccountId}: {Amount} (with resilience)", accountId, amount);
        }
        catch (BrokenCircuitException ex)
        {
            _logger.LogError(ex, "Circuit breaker is open for Accounts MS");
            throw new InvalidOperationException("Accounts MS temporarily unavailable (circuit breaker open)", ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP debit failed for account {AccountId}", accountId);
            throw new InvalidOperationException($"Failed to debit account {accountId}", ex);
        }
    }

    public async Task CreditAsync(Guid accountId, decimal amount, CancellationToken cancellationToken = default)
    {
        try
        {
            var context = new Polly.Context { { "Logger", _logger } };

            var response = await _resiliencePolicy.ExecuteAsync(
                async (ctx, ct) =>
                {
                    var request = new HttpRequestMessage(HttpMethod.Post, $"/{accountId}/credit")
                    {
                        Content = new StringContent(
                            JsonSerializer.Serialize(new { amount }),
                            System.Text.Encoding.UTF8,
                            "application/json")
                    };
                    return await _httpClient.SendAsync(request, ct);
                },
                context,
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new KeyNotFoundException($"Account {accountId} not found");

            response.EnsureSuccessStatusCode();
            _logger.LogInformation("Credit successful for account {AccountId}: {Amount} (with resilience)", accountId, amount);
        }
        catch (BrokenCircuitException ex)
        {
            _logger.LogError(ex, "Circuit breaker is open for Accounts MS");
            throw new InvalidOperationException("Accounts MS temporarily unavailable (circuit breaker open)", ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP credit failed for account {AccountId}", accountId);
            throw new InvalidOperationException($"Failed to credit account {accountId}", ex);
        }
    }

    private record AccountData(Guid Id, Guid UserId, string AccountNumber, decimal Balance);
}
