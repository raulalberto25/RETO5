namespace FinBank.TransfersService.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Polly;
using Polly.CircuitBreaker;
using RabbitMQ.Client;
using Application.Ports;
using Http;
using Messaging;

/// <summary>
/// Dependency injection setup for transfers module
/// Includes resilience policies, RabbitMQ, and background services
/// </summary>
public static class TransfersModuleExtensions
{
    public static IServiceCollection AddTransfersModule(
        this IServiceCollection services,
        string connectionString,
        string accountsServiceUrl)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string cannot be null or empty", nameof(connectionString));
        if (string.IsNullOrWhiteSpace(accountsServiceUrl))
            throw new ArgumentException("Accounts service URL cannot be null or empty", nameof(accountsServiceUrl));

        // Database
        services.AddDbContext<TransfersDbContext>(options =>
            options.UseNpgsql(connectionString,
                npgsqlOptions => npgsqlOptions
                    .MigrationsHistoryTable("__ef_migrations_history", "transfers")));

        // RabbitMQ Connection Factory
        services.AddSingleton<IConnectionFactory>(sp =>
        {
            var factory = new ConnectionFactory()
            {
                HostName = "rabbitmq",
                Port = 5672,
                UserName = "guest",
                Password = "guest",
                AutomaticRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(10)
            };
            return factory;
        });

        // HttpClient with resilience (Polly policies applied in HttpAccountsAdapter)
        // Circuit Breaker: 5 failures → open for 30s
        // Retry: 3 attempts with exponential backoff (1s, 2s, 4s)
        // Timeout: 30s per request
        services.AddHttpClient<IAccountsPort, HttpAccountsAdapter>(client =>
        {
            client.BaseAddress = new Uri(accountsServiceUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        // Repositories
        services.AddScoped<ITransfersRepository, TransfersRepository>();

        // Event Publisher
        services.AddScoped<IEventPublisher, RabbitMqPublisher>();

        // Use Case
        services.AddScoped<TransferUseCase>();

        // Background Services
        services.AddHostedService<OutboxWorker>();

        return services;
    }
}
