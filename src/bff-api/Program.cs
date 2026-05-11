using Azure.Identity;
using Azure.Storage.Blobs;
using Contoso.BffApi;
using Contoso.BffApi.Endpoints;
using Contoso.BffApi.HealthChecks;
using Contoso.BffApi.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Identity.Web;
using System.Threading.RateLimiting;

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

// Cap incoming request bodies. The BFF only accepts small JSON payloads
// (chat messages, order requests). The chat-message guard via
// ConversationLimits.ExceedsMessageLimit is enforced AFTER deserialization;
// this Kestrel-level cap protects the deserializer itself against a hostile
// or buggy client that submits a multi-MB JSON blob.
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 1 * 1024 * 1024; // 1 MiB
});

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
ConfigureRateLimiting(builder);

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

        // Do NOT leak ex.Message to network clients — it can contain payload
        // fragments, file paths, secrets surfaced via wrapping exceptions.
        // Type name is low-sensitivity diagnostic info; full details stay in
        // the server log (logger.LogError above) tied to the trace id.
        // traceId uses the W3C distributed trace id when an Activity is
        // active so operators can find the matching trace in APM directly;
        // falls back to the per-connection ASP.NET id if telemetry is off.
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new
        {
            error = ex?.GetType().Name ?? "InternalServerError",
            message = "An internal error occurred. See server logs for details.",
            traceId = System.Diagnostics.Activity.Current?.TraceId.ToString()
                ?? context.TraceIdentifier,
            requestId = context.TraceIdentifier,
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

// Rate limiting MUST run after auth so we can partition by the
// authenticated customer id (not just the source IP, which is shared
// behind a corporate NAT or a CDN).
app.UseRateLimiter();

app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

var api = app.MapGroup("/api/v1");
var chatEndpoint = api.MapChatEndpoint();
var (conversationsEndpoint, conversationEndpoint) = api.MapConversationEndpoints();
var imageEndpoint = api.MapImageEndpoint();
var (meEndpoint, customerEndpoint, ordersEndpoint, productsEndpoint, productEndpoint, placeOrderEndpoint) = api.MapCustomerEndpoints();
var (submitRefundEndpoint, listPendingEndpoint, submitDecisionEndpoint, getOutcomeEndpoint) = api.MapOperationsEndpoints();

if (useEntraAuth)
{
    // Chat is intentionally NOT RequireAuthorization — the endpoint
    // accepts both authenticated callers (real customer id from JWT)
    // and anonymous callers carrying an X-Guest-Session-Id header
    // (resolved to a "guest-{token}" customer id by CustomerContext).
    // Per-customer state and downstream tool gating still apply.
    chatEndpoint.AllowAnonymous();
    conversationsEndpoint.RequireAuthorization();
    conversationEndpoint.RequireAuthorization();
    meEndpoint.RequireAuthorization();
    customerEndpoint.RequireAuthorization();
    ordersEndpoint.RequireAuthorization();
    placeOrderEndpoint.RequireAuthorization();
    // Catalog browsing (products + product images) is public-by-design.
    // The Blazor UI exposes Home / Browse / ProductDetail to anonymous
    // visitors so the site behaves like a normal e-commerce store; only
    // per-customer actions (cart, checkout, orders, profile) require
    // sign-in. <img src=...> also can't attach a Bearer token.
    productsEndpoint.AllowAnonymous();
    productEndpoint.AllowAnonymous();
    imageEndpoint.AllowAnonymous();

    // Operations dashboard is operator-only. The submit-refund endpoint
    // is also gated — fraud reviews start with someone (or some service)
    // posting an alert, never an anonymous request.
    submitRefundEndpoint.RequireAuthorization();
    listPendingEndpoint.RequireAuthorization();
    submitDecisionEndpoint.RequireAuthorization();
    getOutcomeEndpoint.RequireAuthorization();
}

// Apply rate-limit policies last so they cover both authed and anonymous
// callers. The "chat" policy is the most expensive (model calls + tool
// fan-out) and gets the tightest budget.
chatEndpoint.RequireRateLimiting("chat");
conversationsEndpoint.RequireRateLimiting("read");
conversationEndpoint.RequireRateLimiting("read");
meEndpoint.RequireRateLimiting("read");
customerEndpoint.RequireRateLimiting("read");
ordersEndpoint.RequireRateLimiting("read");
placeOrderEndpoint.RequireRateLimiting("write");
productsEndpoint.RequireRateLimiting("read");
productEndpoint.RequireRateLimiting("read");
imageEndpoint.RequireRateLimiting("read");

// Operator-side endpoints. Submitting a refund alert and submitting a
// decision are write operations; listing pending reviews and fetching an
// outcome are reads.
submitRefundEndpoint.RequireRateLimiting("write");
listPendingEndpoint.RequireRateLimiting("read");
submitDecisionEndpoint.RequireRateLimiting("write");
getOutcomeEndpoint.RequireRateLimiting("read");

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
            // DoS guard: cap upstream response body at 10 MB. The CRM API
            // returns customer/order JSON which never legitimately exceeds
            // a few hundred KB; without this cap a compromised or buggy
            // upstream could cause OutOfMemoryException via JsonNode.Parse.
            client.MaxResponseContentBufferSize = 10 * 1024 * 1024;
        })
        .AddHttpMessageHandler<CustomerHeaderHandler>();

    builder.Services.AddHttpClient<OrchestratorClient>(client =>
        {
            var baseUrl = Program.GetConfigOrDefault(builder.Configuration, "Orchestrator:BaseUrl", "http://localhost:5006");
            client.BaseAddress = new Uri(baseUrl);
        })
        .AddHttpMessageHandler<CustomerHeaderHandler>();

    // FraudWorkflowClient is intentionally NOT decorated with the
    // CustomerHeaderHandler — refund decisions are made by *operators*
    // (or the workflow itself), not by the customer who triggered the
    // refund. Forwarding the customer header would mis-attribute audit
    // records inside the workflow.
    builder.Services.AddHttpClient<FraudWorkflowClient>(client =>
        {
            var baseUrl = Program.GetConfigOrDefault(builder.Configuration, "FraudWorkflow:BaseUrl", "http://localhost:5010");
            client.BaseAddress = new Uri(baseUrl);
            // DoS guard for the same reason as CrmApiClient — operations
            // payloads are always small (sub-100KB), 1 MB is a generous cap.
            client.MaxResponseContentBufferSize = 1 * 1024 * 1024;
        });
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

static void ConfigureRateLimiting(WebApplicationBuilder builder)
{
    // Per-customer fixed-window limits. Partitioning by the resolved
    // customer id (header in dev, JWT subject in production) means a
    // single chatty user cannot exhaust the budget for everyone behind
    // the same NAT or CDN edge. When no customer id is present we fall
    // back to the source IP, which matches anonymous catalog browsing.
    //
    // Defaults are deliberately generous — these are guard-rails against
    // pathological behaviour, not throttles for normal use. Operators
    // can tune via configuration if real traffic patterns require it.
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        static string PartitionKey(HttpContext ctx)
        {
            var customerCtx = ctx.RequestServices.GetService<CustomerContext>();
            var id = customerCtx?.GetCustomerId();

            // Authenticated callers get a per-customer bucket. Guests
            // (anonymous chat) DO NOT — their customer id is the
            // caller-supplied X-Guest-Session-Id token, and an attacker
            // can rotate it on every request to mint fresh per-bucket
            // budgets and bypass the cap. Fall through to the per-IP
            // bucket below, which is stable across token rotation.
            if (!string.IsNullOrWhiteSpace(id) && !GuestId.IsGuest(id))
            {
                return $"cust:{id}";
            }
            var ip = ctx.Connection.RemoteIpAddress?.ToString();
            if (!string.IsNullOrWhiteSpace(ip))
            {
                return $"ip:{ip}";
            }
            // Last-resort: per-connection key. Stops one anonymous caller
            // with a missing IP (e.g., Kestrel in-process tests) from
            // sharing a global "ip:unknown" bucket with every other such
            // caller and tripping a self-DoS.
            return $"conn:{ctx.Connection.Id}";
        }

        // "chat" — the LLM hot path. Each request fans out to the
        // orchestrator + at least one specialist agent + one or more
        // MCP servers. 200/min/customer is roughly one chat every 300ms,
        // far above any human typing cadence.
        options.AddPolicy("chat", ctx => RateLimitPartition.GetFixedWindowLimiter(
            PartitionKey(ctx),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 200,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));

        // "read" — cheap GETs (product catalog, conversation list, etc).
        // 1200/min ≈ 20/sec/customer comfortably handles a paginating UI.
        options.AddPolicy("read", ctx => RateLimitPartition.GetFixedWindowLimiter(
            PartitionKey(ctx),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 1200,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));

        // "write" — POSTs that mutate Cosmos (place order). Tighter
        // because each request consumes RUs and triggers a write.
        options.AddPolicy("write", ctx => RateLimitPartition.GetFixedWindowLimiter(
            PartitionKey(ctx),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));
    });
}

// `GetConfigOrDefault` lives on Program (exposed via partial) so integration
// tests can reflect on it.
public partial class Program
{
    internal static string GetConfigOrDefault(Microsoft.Extensions.Configuration.IConfiguration config, string key, string defaultValue)
        => config[key] is { Length: > 0 } value ? value : defaultValue;
}
