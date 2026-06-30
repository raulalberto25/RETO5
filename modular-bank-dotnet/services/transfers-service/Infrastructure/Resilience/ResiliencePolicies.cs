namespace FinBank.TransfersService.Infrastructure.Resilience;

using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;

/// <summary>
/// Polly resilience policies for distributed calls
/// Applied to: HttpAccountsAdapter (calls to Accounts MS)
/// </summary>
public static class ResiliencePolicies
{
    /// <summary>
    /// Retry policy: 3 attempts with exponential backoff
    /// Waits: 1s, 2s, 4s
    /// </summary>
    public static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
    {
        return Policy
            .Handle<HttpRequestException>()
            .Or<OperationCanceledException>()
            .OrResult<HttpResponseMessage>(r => !r.IsSuccessStatusCode && r.StatusCode != System.Net.HttpStatusCode.NotFound)
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: retryAttempt =>
                    TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (outcome, timespan, retryCount, context) =>
                {
                    var logger = context.GetLogger();
                    logger?.LogWarning(
                        "Retry {RetryCount} after {Delay}ms due to: {Reason}",
                        retryCount,
                        timespan.TotalMilliseconds,
                        outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString());
                });
    }

    /// <summary>
    /// Circuit breaker policy: opens after 5 failures in 30 seconds
    /// Half-open state: probes every 10 seconds
    /// </summary>
    public static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy()
    {
        return Policy
            .Handle<HttpRequestException>()
            .Or<OperationCanceledException>()
            .OrResult<HttpResponseMessage>(r => !r.IsSuccessStatusCode && r.StatusCode != System.Net.HttpStatusCode.NotFound)
            .CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: 5,
                durationOfBreak: TimeSpan.FromSeconds(30),
                onBreak: (outcome, duration, context) =>
                {
                    var logger = context.GetLogger();
                    logger?.LogError(
                        "Circuit breaker opened for {Duration}s after failures: {Reason}",
                        duration.TotalSeconds,
                        outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString());
                },
                onReset: (context) =>
                {
                    var logger = context.GetLogger();
                    logger?.LogInformation("Circuit breaker closed, resuming calls");
                },
                onHalfOpen: (context) =>
                {
                    var logger = context.GetLogger();
                    logger?.LogInformation("Circuit breaker half-open, testing next call");
                });
    }

    /// <summary>
    /// Timeout policy: 30 seconds max per request
    /// </summary>
    public static IAsyncPolicy<HttpResponseMessage> GetTimeoutPolicy()
    {
        return Policy
            .TimeoutAsync<HttpResponseMessage>(
                timeout: TimeSpan.FromSeconds(30),
                timeoutStrategy: TimeoutStrategy.Optimistic);
    }

    /// <summary>
    /// Combined resilience policy: Retry → Circuit Breaker → Timeout
    /// Order: Timeout wraps (Circuit Breaker wraps Retry)
    /// </summary>
    public static IAsyncPolicy<HttpResponseMessage> GetCombinedPolicy()
    {
        var retryPolicy = GetRetryPolicy();
        var circuitBreakerPolicy = GetCircuitBreakerPolicy();
        var timeoutPolicy = GetTimeoutPolicy();

        // Wrap in order: timeout (outer) → circuit breaker → retry (inner)
        return Policy.WrapAsync(
            timeoutPolicy,
            circuitBreakerPolicy.WrapAsync(retryPolicy));
    }
}

/// <summary>
/// Extension to get logger from Polly context
/// </summary>
internal static class PollyContextExtensions
{
    private const string LoggerKey = "Logger";

    public static void SetLogger(this Polly.Context context, ILogger logger)
    {
        context[LoggerKey] = logger;
    }

    public static ILogger? GetLogger(this Polly.Context context)
    {
        return context.TryGetValue(LoggerKey, out var logger) ? logger as ILogger : null;
    }
}
