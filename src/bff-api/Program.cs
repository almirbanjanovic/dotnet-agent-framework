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

// IMPORTANT: ExceptionHandler must run BEFORE UseCors so that the response
// produced for an unhandled exception still flows through the CORS middleware
// and gets the Access-Control-Allow-Origin header. Without this, a 500 from
// (for example) the orchestrator surfaces in the browser as a generic CORS
// failure with no useful message.
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var feature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
        var ex = feature?.Error;
        var logger = context.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Contoso.BffApi.UnhandledException");
        logger.LogError(ex, "Unhandled exception on {Path}", context.Request.Path);

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new
        {
            error = ex?.GetType().Name ?? "InternalServerError",
            message = ex?.Message ?? "An unhandled exception occurred.",
            path = context.Request.Path.Value
        });
    });
});

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
var (meEndpoint, customerEndpoint, ordersEndpoint, productsEndpoint, productEndpoint, placeOrderEndpoint) = api.MapCustomerEndpoints();

if (useEntraAuth)
{
    chatEndpoint.RequireAuthorization();
    conversationsEndpoint.RequireAuthorization();
    conversationEndpoint.RequireAuthorization();
    meEndpoint.RequireAuthorization();
    customerEndpoint.RequireAuthorization();
    ordersEndpoint.RequireAuthorization();
    productsEndpoint.RequireAuthorization();
    productEndpoint.RequireAuthorization();
    placeOrderEndpoint.RequireAuthorization();
    // Product images are public-by-design (in production they would live on
    // a CDN / public blob container). <img src=...> can't attach a Bearer
    // token, so requiring auth here would 401 every product card.
    imageEndpoint.AllowAnonymous();
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

    // NOTE: the standard resilience handler is added once for all clients in
    // ServiceDefaults.ConfigureHttpClientDefaults with agent-friendly timeouts.
    // Do NOT add it again here — chaining a second .AddStandardResilienceHandler()
    // stacks a second pipeline that uses the unconfigured 30-second defaults.
    builder.Services.AddHttpClient<CrmApiClient>(client =>
        {
            var baseUrl = Program.GetConfigOrDefault(builder.Configuration, "CrmApi:BaseUrl", "http://localhost:5001");
            client.BaseAddress = new Uri(baseUrl);
        })
        .AddHttpMessageHandler<CustomerHeaderHandler>();

    builder.Services.AddHttpClient<OrchestratorClient>(client =>
        {
            var baseUrl = Program.GetConfigOrDefault(builder.Configuration, "Orchestrator:BaseUrl", "http://localhost:5006");
            client.BaseAddress = new Uri(baseUrl);
        })
        .AddHttpMessageHandler<CustomerHeaderHandler>();
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

    // Microsoft.Identity.Web requires AzureAd:Instance to validate tokens.
    // Default to public Entra so neither developers nor CI/CD have to set
    // this for the common case; sovereign clouds can override in config.
    if (string.IsNullOrWhiteSpace(builder.Configuration["AzureAd:Instance"]))
    {
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AzureAd:Instance"] = "https://login.microsoftonline.com/"
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
