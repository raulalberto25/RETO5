using ModularBank.Modules.Auth.Infrastructure;
using ModularBank.Modules.Auth.Api;
using ModularBank.Modules.Notifications.Api;
using ModularBank.Modules.Accounts.Infrastructure;
using ModularBank.Modules.Accounts.Api;
using ModularBank.Modules.Transfers.Infrastructure;
using ModularBank.Modules.Transfers.Api;
using ModularBank.Modules.Notifications.Infrastructure;
using ModularBank.Modules.Audit.Infrastructure;
using ModularBank.Modules.Audit.Api;
using ModularBank.Shared.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

var jwtSecret = builder.Configuration["Jwt:Secret"];
if (string.IsNullOrWhiteSpace(jwtSecret) || jwtSecret.Length < 32)
    throw new InvalidOperationException("Jwt:Secret must be configured and at least 32 characters.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
            ValidateIssuer = false,   // TODO: set ValidIssuer before microservice extraction
            ValidateAudience = false, // TODO: set ValidAudience before microservice extraction
            ValidateLifetime = true,
            RequireSignedTokens = true,
            ClockSkew = TimeSpan.Zero
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddSingleton<JwtUtil>();

builder.Services.AddAuthModule(connectionString);

// Feature flag: USE_ACCOUNTS_MS=true for HTTP, false for in-process
var useAccountsMS = builder.Configuration.GetValue<bool>("Features:UseAccountsMS", false);
if (useAccountsMS)
{
    // HTTP mode: call Accounts MS
    var accountsServiceUrl = builder.Configuration["AccountsService:Url"] ?? "http://localhost:5001";
    builder.Services.AddHttpClient<IAccountsService, HttpAccountsService>(client =>
    {
        client.BaseAddress = new Uri(accountsServiceUrl);
        client.Timeout = TimeSpan.FromSeconds(30);
    });
}
else
{
    // Local mode: in-process
    builder.Services.AddAccountsModule(connectionString);
}

builder.Services.AddTransfersModule(connectionString);
builder.Services.AddNotificationsModule(connectionString);
builder.Services.AddAuditModule(connectionString);

// RabbitMQ Connection Factory
builder.Services.AddSingleton<IConnectionFactory>(sp =>
{
    var factory = new RabbitMQ.Client.ConnectionFactory()
    {
        HostName = builder.Configuration["RabbitMQ:Host"] ?? "localhost",
        Port = builder.Configuration.GetValue<int>("RabbitMQ:Port", 5672),
        UserName = builder.Configuration["RabbitMQ:Username"] ?? "guest",
        Password = builder.Configuration["RabbitMQ:Password"] ?? "guest",
        AutomaticRecoveryEnabled = true,
        NetworkRecoveryInterval = TimeSpan.FromSeconds(10)
    };
    return factory;
});

// Background consumers for RabbitMQ events
builder.Services.AddHostedService<ModularBank.Modules.Notifications.Infrastructure.NotificationsConsumer>();
builder.Services.AddHostedService<ModularBank.Modules.Audit.Infrastructure.AuditConsumer>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var contexts = new DbContext[]
    {
        scope.ServiceProvider.GetRequiredService<AuthDbContext>(),
        scope.ServiceProvider.GetRequiredService<AccountsDbContext>(),
        scope.ServiceProvider.GetRequiredService<TransfersDbContext>(),
        scope.ServiceProvider.GetRequiredService<NotificationsDbContext>(),
        scope.ServiceProvider.GetRequiredService<AuditDbContext>()
    };
    foreach (var ctx in contexts)
        ctx.Database.Migrate();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => "ok");
app.MapAuthEndpoints();
app.MapAccountsEndpoints();
app.MapTransfersEndpoints();
app.MapNotificationsEndpoints();
app.MapAuditEndpoints();

app.Run();

public partial class Program { }
