using ModularBank.Modules.Accounts.Application;
using ModularBank.Modules.Accounts.Application.Dto;
using ModularBank.Shared.Domain;
using System.Net;
using System.Text;
using System.Text.Json;

namespace ModularBank.Modules.Accounts.Infrastructure;

/// <summary>
/// Adapter: implements IAccountsService via HTTP calls to Accounts MS.
/// Used in microservices mode; phase 1 transition from monolith.
/// </summary>
public class HttpAccountsService : IAccountsService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<HttpAccountsService> _logger;

    public HttpAccountsService(HttpClient httpClient, ILogger<HttpAccountsService> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<AccountSummary> CreateAccountAsync(Guid userId)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("/accounts", new { });
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var account = JsonSerializer.Deserialize<AccountSummary>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidOperationException("Failed to deserialize account response");

            _logger.LogInformation("Account created via HTTP: {AccountId}", account.Id);
            return account;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP request failed to Accounts MS");
            throw new InvalidOperationException("Failed to create account via Accounts MS", ex);
        }
    }

    public async Task<Money> GetBalanceAsync(Guid accountId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/accounts/{accountId}/balance");

            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new KeyNotFoundException($"Account {accountId} not found");

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var balanceResponse = JsonSerializer.Deserialize<BalanceResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidOperationException("Failed to deserialize balance response");

            return Money.Of(decimal.Parse(balanceResponse.Amount));
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP request failed to get balance for account {AccountId}", accountId);
            throw new InvalidOperationException($"Failed to get balance for account {accountId}", ex);
        }
    }

    public async Task DebitAsync(Guid accountId, Money amount, string? reference)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"/accounts/{accountId}/debit")
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { amount = amount.Amount, reference }),
                    Encoding.UTF8,
                    "application/json")
            };

            var response = await _httpClient.SendAsync(request);

            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new KeyNotFoundException($"Account {accountId} not found");

            if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new InvalidOperationException($"Insufficient funds: {errorContent}");
            }

            response.EnsureSuccessStatusCode();
            _logger.LogInformation("Debit successful: {AccountId} - {Amount}", accountId, amount);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP request failed to debit account {AccountId}", accountId);
            throw new InvalidOperationException($"Failed to debit account {accountId}", ex);
        }
    }

    public async Task CreditAsync(Guid accountId, Money amount, string? reference)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"/accounts/{accountId}/credit")
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { amount = amount.Amount, reference }),
                    Encoding.UTF8,
                    "application/json")
            };

            var response = await _httpClient.SendAsync(request);

            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new KeyNotFoundException($"Account {accountId} not found");

            response.EnsureSuccessStatusCode();
            _logger.LogInformation("Credit successful: {AccountId} - {Amount}", accountId, amount);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP request failed to credit account {AccountId}", accountId);
            throw new InvalidOperationException($"Failed to credit account {accountId}", ex);
        }
    }

    public async Task<List<AccountSummary>> FindByOwnerAsync(Guid userId)
    {
        try
        {
            var response = await _httpClient.GetAsync("/accounts");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var accounts = JsonSerializer.Deserialize<List<AccountSummary>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new List<AccountSummary>();

            return accounts;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP request failed to find accounts for user {UserId}", userId);
            throw new InvalidOperationException($"Failed to find accounts for user {userId}", ex);
        }
    }

    private record BalanceResponse
    {
        public string Amount { get; set; } = "";
    }
}
