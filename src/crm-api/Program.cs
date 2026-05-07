using Azure.Identity;
using Contoso.CrmApi;
using Contoso.CrmApi.Endpoints;
using Contoso.CrmApi.HealthChecks;
using Contoso.CrmApi.Middleware;
using Contoso.CrmApi.Services;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Threading.RateLimiting;

// ─────────────────────────────────────────────────────────────────────────
// CRM API — REST surface over Cosmos DB. Backend for the CRM MCP server.
//
// Composition root only. Concrete logic lives in:
//   Models/         → Customer, Order, Product, Promotion, SupportTicket
//   Services/       → ICosmosService + Cosmos and InMemory implementations,
//                     CustomerContext (per-request identity)
//   Endpoints/      → One Map*Endpoints class per resource
//   Middleware/     → GlobalExceptionHandler
//   HealthChecks/   → CosmosHealthCheck for /ready
// ─────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Cap incoming request bodies. The CRM API only accepts small JSON payloads
// (orders, support tickets); this guard catches a hostile / buggy client
// trying to slip a huge `shipping_address` or unknown-field blob past the
// per-endpoint validation by exhausting memory during model binding.
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 256 * 1024; // 256 KiB
});

// Per-request customer identity from the X-Customer-Entra-Id header.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CustomerContext>();

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.UseUtcTimestamp = true;
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
});

// All Azure clients use a single DefaultAzureCredential (no API keys).
var tenantId = builder.Configuration["AzureAd:TenantId"];
var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = tenantId });

// DataMode=InMemory swaps in a stub so devs can run without provisioning Cosmos.
var dataMode = builder.Configuration["DataMode"];
var useInMemory = string.Equals(dataMode, "InMemory", StringComparison.OrdinalIgnoreCase);

if (useInMemory)
{
    builder.Services.AddSingleton<ICosmosService, InMemoryCrmDataService>();
}
else
{
    var cosmosEndpoint = builder.Configuration["CosmosDb:Endpoint"]
        ?? throw new InvalidOperationException("CosmosDb:Endpoint configuration is required.");

    builder.Services.AddSingleton(_ => new CosmosClient(cosmosEndpoint, credential, new CosmosClientOptions
    {
        SerializerOptions = new CosmosSerializationOptions { PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase },
        ConnectionMode = ConnectionMode.Direct,
        ApplicationName = "Contoso.CrmApi"
    }));
    builder.Services.AddScoped<ICosmosService, CosmosService>();
}

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// Per-customer rate limiting. The CRM API only sees inbound traffic from
// BFF and CRM-MCP (both behind auth), but a runaway BFF caller or compromised
// agent must not be able to exhaust Cosmos RUs. Generous defaults — these
// are guard-rails, not throttles.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Single global policy partitioned by the customer header set by BFF.
    // When the header is missing (health probes, internal callers) we
    // fall back to the source IP, then per-connection id so a missing IP
    // can't collapse all anonymous callers into one shared bucket.
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
    {
        // Health probes are explicitly NOT rate-limited — a flood from a
        // misbehaving probe must not trip the limiter and mask real
        // service health by 429-ing legitimate liveness/readiness checks.
        var path = ctx.Request.Path;
        if (path.StartsWithSegments("/health") || path.StartsWithSegments("/ready"))
        {
            return RateLimitPartition.GetNoLimiter("health");
        }

        var customerId = ctx.Request.Headers["X-Customer-Entra-Id"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(customerId))
        {
            customerId = ctx.Connection.RemoteIpAddress?.ToString();
        }
        if (string.IsNullOrWhiteSpace(customerId))
        {
            customerId = $"conn:{ctx.Connection.Id}";
        }
        return RateLimitPartition.GetFixedWindowLimiter(
            customerId,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 600,                     // 10 req/sec/customer
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            });
    });
});

if (useInMemory)
{
    // No external dependency — readiness is just process-up.
    builder.Services.AddHealthChecks()
        .AddCheck("in-memory-crm", () => HealthCheckResult.Healthy("In-memory CRM data service."), tags: ["ready"]);
}
else
{
    builder.Services.AddHealthChecks()
        .AddCheck<CosmosHealthCheck>("cosmos-db", tags: ["ready"]);
}

var app = builder.Build();

app.UseExceptionHandler();

// Apply rate limiting before health checks so /health and /ready are
// also bounded. They're cheap, but a flood of probes could still mask
// the real problem in a metrics dashboard.
app.UseRateLimiter();

app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

app.MapCustomerEndpoints();
app.MapOrderEndpoints();
app.MapProductEndpoints();
app.MapPromotionEndpoints();
app.MapSupportTicketEndpoints();

app.Run();

// Make Program accessible for integration tests.
public partial class Program { }
