var builder = WebApplication.CreateBuilder(args);

// OpenTelemetry
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("api-gateway"))
    .WithTracing(b => b
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(opts => opts.Endpoint = new Uri(
            builder.Configuration["OpenTelemetry:ExporterEndpoint"] ?? "http://localhost:4317")))
    .WithMetrics(b => b
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddOtlpExporter(opts => opts.Endpoint = new Uri(
            builder.Configuration["OpenTelemetry:ExporterEndpoint"] ?? "http://localhost:4317")));

// Logging
builder.Logging.AddConsole();

// YARP Reverse Proxy
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

// Health check
app.MapGet("/health", () => "ok")
    .WithName("Health")
    .WithOpenApi();

// YARP routing
app.MapReverseProxy();

app.Run();

public partial class Program { }
