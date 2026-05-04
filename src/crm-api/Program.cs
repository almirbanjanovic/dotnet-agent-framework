using Azure.Identity;
using Contoso.CrmApi;
using Contoso.CrmApi.Endpoints;
using Contoso.CrmApi.HealthChecks;
using Contoso.CrmApi.Middleware;
using Contoso.CrmApi.Services;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Diagnostics.HealthChecks;

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
