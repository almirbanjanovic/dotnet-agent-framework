using Azure.Identity;
using Azure.Storage.Blobs;
using Contoso.BffApi;
using Contoso.BffApi.Endpoints;
using Contoso.BffApi.HealthChecks;
using Contoso.BffApi.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Identity.Web;

// ─────────────────────────────────────────────────────────────────────────
// BFF API — backend-for-frontend that the Blazor UI talks to.
//
// Responsibilities (all in extracted files; this is just the composition root):
//   Models/         → wire DTOs (ChatRequest/Response, Conversation, AgentChatResponse)
//   Services/       → CrmApiClient, OrchestratorClient, IConversationStore +
//                     impls, IImageService + impls, CustomerContext,
//                     CustomerHeaderHandler, FileNameValidator
//   HealthChecks/   → Cosmos / CRM API / Orchestrator probes
//   Endpoints/      → Chat, Conversations, Images, Customers
// ─────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile(
    $"appsettings.{builder.Environment.EnvironmentName}.json",
    optional: true,
    reloadOnChange: true);

builder.AddServiceDefaults();
builder.Services.AddHttpContextAccessor();

var dataMode = builder.Configuration["DataMode"];
var useInMemory = string.Equals(dataMode, "InMemory", StringComparison.OrdinalIgnoreCase);

// Auth and storage are independent: a developer can opt into real Microsoft
// Entra ID sign-in on the Local Track (InMemory data + JWT) by setting
// AzureAd:Enabled = true. By default, InMemory data implies header-based
// dev auth and Cosmos data implies JWT.
var useEntraAuth = string.Equals(
    builder.Configuration["AzureAd:Enabled"],
    "true",
    StringComparison.OrdinalIgnoreCase) || !useInMemory;

ConfigureCustomerContext(builder, useEntraAuth);
ConfigureHttpClients(builder);
ConfigureDataServices(builder, useInMemory);
ConfigureAuth(builder, useEntraAuth);
ConfigureHealthChecks(builder, useInMemory);
ConfigureCors(builder);

var app = builder.Build();

app.UseCors("BlazorUI");

if (useEntraAuth)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

var api = app.MapGroup("/api/v1");
var chatEndpoint = api.MapChatEndpoint();
var (conversationsEndpoint, conversationEndpoint) = api.MapConversationEndpoints();
var imageEndpoint = api.MapImageEndpoint();
var (customerEndpoint, ordersEndpoint) = api.MapCustomerEndpoints();

if (useEntraAuth)
{
    chatEndpoint.RequireAuthorization();
    conversationsEndpoint.RequireAuthorization();
    conversationEndpoint.RequireAuthorization();
    customerEndpoint.RequireAuthorization();
    ordersEndpoint.RequireAuthorization();
    imageEndpoint.RequireAuthorization();
}

app.Run();

// ── Local helpers (purely organisational; not exposed for testing) ────────

static void ConfigureCustomerContext(WebApplicationBuilder builder, bool useEntraAuth)
{
    var customerMap = builder.Configuration
        .GetSection("AzureAd:CustomerMap")
        .Get<Dictionary<string, string>>() ?? new Dictionary<string, string>();

    builder.Services.AddSingleton(sp => new CustomerContext(
        sp.GetRequiredService<IHttpContextAccessor>(),
        useHeader: !useEntraAuth,
        customerMap: customerMap));
}

static void ConfigureHttpClients(WebApplicationBuilder builder)
{
    builder.Services.AddTransient<CustomerHeaderHandler>();

    builder.Services.AddHttpClient<CrmApiClient>(client =>
        {
            var baseUrl = Program.GetConfigOrDefault(builder.Configuration, "CrmApi:BaseUrl", "http://localhost:5001");
            client.BaseAddress = new Uri(baseUrl);
        })
        .AddHttpMessageHandler<CustomerHeaderHandler>()
        .AddStandardResilienceHandler();

    builder.Services.AddHttpClient<OrchestratorClient>(client =>
        {
            var baseUrl = Program.GetConfigOrDefault(builder.Configuration, "Orchestrator:BaseUrl", "http://localhost:5006");
            client.BaseAddress = new Uri(baseUrl);
        })
        .AddHttpMessageHandler<CustomerHeaderHandler>()
        .AddStandardResilienceHandler();
}

static void ConfigureDataServices(WebApplicationBuilder builder, bool useInMemory)
{
    if (useInMemory)
    {
        builder.Services.AddSingleton<IConversationStore, InMemoryConversationStore>();
        builder.Services.AddSingleton<IImageService, LocalFileImageService>();
        return;
    }

    var tenantId = builder.Configuration["AzureAd:TenantId"];
    var credential = string.IsNullOrWhiteSpace(tenantId)
        ? new DefaultAzureCredential()
        : new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = tenantId });

    var cosmosEndpoint = builder.Configuration["CosmosDb:AgentsEndpoint"]
        ?? throw new InvalidOperationException("CosmosDb:AgentsEndpoint configuration is required.");

    builder.Services.AddSingleton(_ => new CosmosClient(cosmosEndpoint, credential, new CosmosClientOptions
    {
        SerializerOptions = new CosmosSerializationOptions { PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase },
        ConnectionMode = ConnectionMode.Direct,
        ApplicationName = "Contoso.BffApi"
    }));
    builder.Services.AddSingleton<IConversationStore, CosmosConversationStore>();

    var storageEndpoint = builder.Configuration["Storage:ImagesEndpoint"]
        ?? throw new InvalidOperationException("Storage:ImagesEndpoint configuration is required.");
    builder.Services.AddSingleton(new BlobServiceClient(new Uri(storageEndpoint), credential));
    builder.Services.AddSingleton<IImageService, BlobImageService>();
}

static void ConfigureAuth(WebApplicationBuilder builder, bool useEntraAuth)
{
    if (!useEntraAuth)
    {
        return;
    }

    var bffClientId = builder.Configuration["AzureAd:BffClientId"];
    if (!string.IsNullOrWhiteSpace(bffClientId))
    {
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AzureAd:ClientId"] = bffClientId
        });
    }

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));
    builder.Services.AddAuthorization();
}

static void ConfigureHealthChecks(WebApplicationBuilder builder, bool useInMemory)
{
    var hc = builder.Services.AddHealthChecks();

    if (useInMemory)
    {
        hc.AddCheck("in-memory-bff", () => HealthCheckResult.Healthy("In-memory BFF services."), tags: ["ready"]);
    }
    else
    {
        hc.AddCheck<CosmosHealthCheck>("cosmos-db", tags: ["ready"]);
    }

    hc.AddCheck<CrmApiHealthCheck>("crm-api", tags: ["ready"]);
    hc.AddCheck<OrchestratorHealthCheck>("orchestrator", tags: ["ready"]);
}

static void ConfigureCors(WebApplicationBuilder builder)
{
    var uiOrigin = builder.Configuration["BlazorUi:Origin"] ?? "http://localhost:5008";
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("BlazorUI", policy =>
            policy.WithOrigins(uiOrigin).AllowAnyHeader().AllowAnyMethod());
    });
}

// `GetConfigOrDefault` lives on Program (exposed via partial) so integration
// tests can reflect on it.
public partial class Program
{
    internal static string GetConfigOrDefault(Microsoft.Extensions.Configuration.IConfiguration config, string key, string defaultValue)
        => config[key] is { Length: > 0 } value ? value : defaultValue;
}
