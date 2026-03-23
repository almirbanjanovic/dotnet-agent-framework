using Azure.Identity;
using Contoso.CrmApi.Endpoints;
using Contoso.CrmApi.Middleware;
using Contoso.CrmApi.Services;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

// ── Configuration ──────────────────────────────────────────────────────────
builder.Configuration
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

// ── Logging ────────────────────────────────────────────────────────────────
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.UseUtcTimestamp = true;
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
});

// ── Azure Identity ─────────────────────────────────────────────────────────
var tenantId = builder.Configuration["AzureAd:TenantId"];
var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
{
    TenantId = tenantId
});

// ── Cosmos DB ──────────────────────────────────────────────────────────────
var cosmosEndpoint = builder.Configuration["CosmosDb:Endpoint"]
    ?? throw new InvalidOperationException("CosmosDb:Endpoint configuration is required.");

builder.Services.AddSingleton(sp =>
{
    var clientOptions = new CosmosClientOptions
    {
        SerializerOptions = new CosmosSerializationOptions
        {
            PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
        },
        ConnectionMode = ConnectionMode.Direct,
        ApplicationName = "Contoso.CrmApi"
    };

    return new CosmosClient(cosmosEndpoint, credential, clientOptions);
});

builder.Services.AddScoped<ICosmosService, CosmosService>();

// ── Error Handling ─────────────────────────────────────────────────────────
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// ── Health Checks ──────────────────────────────────────────────────────────
builder.Services.AddHealthChecks()
    .Add(new HealthCheckRegistration(
        "cosmos-db",
        sp =>
        {
            var cosmos = sp.GetRequiredService<ICosmosService>();
            return new CosmosHealthCheck(cosmos);
        },
        failureStatus: HealthStatus.Unhealthy,
        tags: ["ready"]));

var app = builder.Build();

// ── Middleware Pipeline ────────────────────────────────────────────────────
app.UseExceptionHandler();

// ── Health Endpoints ───────────────────────────────────────────────────────
app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false // Liveness — always 200
});

app.MapHealthChecks("/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

// ── API Endpoints ──────────────────────────────────────────────────────────
app.MapCustomerEndpoints();
app.MapOrderEndpoints();
app.MapProductEndpoints();
app.MapPromotionEndpoints();
app.MapSupportTicketEndpoints();

app.Run();

// ── Health Check Implementation ────────────────────────────────────────────
internal sealed class CosmosHealthCheck(ICosmosService cosmosService) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var isHealthy = await cosmosService.CheckConnectivityAsync(cancellationToken);
        return isHealthy
            ? HealthCheckResult.Healthy("Cosmos DB is reachable.")
            : HealthCheckResult.Unhealthy("Cosmos DB is not reachable.");
    }
}

// Make Program accessible for integration tests
public partial class Program { }
