namespace FinBank.AccountsService.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Application;
using Application.Ports;

/// <summary>
/// Dependency injection setup for accounts module.
/// </summary>
public static class AccountsModuleExtensions
{
    public static IServiceCollection AddAccountsModule(
        this IServiceCollection services,
        string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string cannot be null or empty", nameof(connectionString));

        // Database
        services.AddDbContext<AccountsDbContext>(options =>
            options.UseNpgsql(connectionString,
                npgsqlOptions => npgsqlOptions
                    .MigrationsHistoryTable("__ef_migrations_history", "accounts")));

        // Repository (port implementation)
        services.AddScoped<IAccountsRepository, AccountsRepository>();

        // Use case
        services.AddScoped<AccountsUseCase>();

        return services;
    }
}
