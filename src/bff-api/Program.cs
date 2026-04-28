using System.Text.Json;
using Azure.Identity;
using Azure.Storage.Blobs;
using Contoso.BffApi.Models;
using Contoso.BffApi.Services;
using Contoso.ServiceDefaults;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Identity.Web;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile(
    $"appsettings.{builder.Environment.EnvironmentName}.json",
    optional: true,
    reloadOnChange: true);

builder.AddServiceDefaults();

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(5007);
});

builder.Services.AddHttpContextAccessor();

var dataMode = builder.Configuration["DataMode"];
var useInMemory = string.Equals(dataMode, "InMemory", StringComparison.OrdinalIgnoreCase);

builder.Services.AddSingleton(sp => new CustomerContext(
    sp.GetRequiredService<IHttpContextAccessor>(),
    useInMemory));

builder.Services.AddHttpClient<CrmApiClient>(client =>
    {
        var baseUrl = GetConfigOrDefault(builder.Configuration, "CrmApi:BaseUrl", "http://localhost:5001");
        client.BaseAddress = new Uri(baseUrl);
    })
    .AddStandardResilienceHandler();

builder.Services.AddHttpClient<OrchestratorClient>(client =>
    {
        var baseUrl = GetConfigOrDefault(builder.Configuration, "Orchestrator:BaseUrl", "http://localhost:5006");
        client.BaseAddress = new Uri(baseUrl);
    })
    .AddStandardResilienceHandler();

var healthChecks = builder.Services.AddHealthChecks();

if (useInMemory)
{
    builder.Services.AddSingleton<IConversationStore, InMemoryConversationStore>();
    builder.Services.AddSingleton<IImageService, LocalFileImageService>();

    healthChecks.AddCheck("in-memory-bff", () => HealthCheckResult.Healthy("In-memory BFF services."), tags: ["ready"]);
}
else
{
    var tenantId = builder.Configuration["AzureAd:TenantId"];
    var credential = string.IsNullOrWhiteSpace(tenantId)
        ? new DefaultAzureCredential()
        : new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = tenantId });

    var cosmosEndpoint = builder.Configuration["CosmosDb:AgentsEndpoint"]
        ?? throw new InvalidOperationException("CosmosDb:AgentsEndpoint configuration is required.");

    builder.Services.AddSingleton(sp =>
    {
        var clientOptions = new CosmosClientOptions
        {
            SerializerOptions = new CosmosSerializationOptions
            {
                PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
            },
            ConnectionMode = ConnectionMode.Direct,
            ApplicationName = "Contoso.BffApi"
        };

        return new CosmosClient(cosmosEndpoint, credential, clientOptions);
    });

    builder.Services.AddSingleton<IConversationStore, CosmosConversationStore>();

    var storageEndpoint = builder.Configuration["Storage:ImagesEndpoint"]
        ?? throw new InvalidOperationException("Storage:ImagesEndpoint configuration is required.");
    builder.Services.AddSingleton(new BlobServiceClient(new Uri(storageEndpoint), credential));
    builder.Services.AddSingleton<IImageService, BlobImageService>();

    healthChecks.AddCheck<CosmosHealthCheck>("cosmos-db", tags: ["ready"]);

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

healthChecks.AddCheck<CrmApiHealthCheck>("crm-api", tags: ["ready"]);
healthChecks.AddCheck<OrchestratorHealthCheck>("orchestrator", tags: ["ready"]);

var uiOrigin = builder.Configuration["BlazorUi:Origin"] ?? "http://localhost:5008";
builder.Services.AddCors(options =>
{
    options.AddPolicy("BlazorUI", policy =>
        policy.WithOrigins(uiOrigin).AllowAnyHeader().AllowAnyMethod());
});

var app = builder.Build();

app.UseCors("BlazorUI");

if (!useInMemory)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = _ => false
});

app.MapHealthChecks("/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

var api = app.MapGroup("/api/v1");

var chatEndpoint = api.MapPost("/chat", async (
    ChatRequest request,
    IConversationStore conversationStore,
    OrchestratorClient orchestratorClient,
    CustomerContext customerContext,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Message))
    {
        return Results.BadRequest(new { error = "message is required." });
    }

    var customerId = customerContext.GetCustomerId();
    if (string.IsNullOrWhiteSpace(customerId))
    {
        return Results.Unauthorized();
    }

    Conversation? conversation;
    if (string.IsNullOrWhiteSpace(request.ConversationId))
    {
        conversation = await conversationStore.CreateConversationAsync(customerId, cancellationToken);
    }
    else
    {
        conversation = await conversationStore.GetConversationAsync(request.ConversationId, cancellationToken);
        if (conversation is null)
        {
            return Results.NotFound();
        }

        if (!string.Equals(conversation.CustomerId, customerId, StringComparison.OrdinalIgnoreCase))
        {
            return Results.NotFound();
        }
    }

    await conversationStore.AddMessageAsync(
        conversation.Id,
        new ChatMessage("user", request.Message, DateTimeOffset.UtcNow),
        cancellationToken);

    using var response = await orchestratorClient.SendAsync(customerId, request.Message, cancellationToken);
    var payload = await response.Content.ReadAsStringAsync(cancellationToken);

    if (!response.IsSuccessStatusCode)
    {
        var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/json";
        return Results.Content(payload, contentType, statusCode: (int)response.StatusCode);
    }

    AgentChatResponse? agentResponse = null;
    try
    {
        agentResponse = JsonSerializer.Deserialize<AgentChatResponse>(payload, jsonOptions);
    }
    catch (JsonException)
    {
    }

    var assistantMessage = agentResponse?.Response ?? payload;
    var toolCalls = agentResponse?.ToolCalls ?? Array.Empty<ToolCallInfo>();

    await conversationStore.AddMessageAsync(
        conversation.Id,
        new ChatMessage("assistant", assistantMessage, DateTimeOffset.UtcNow),
        cancellationToken);

    return Results.Ok(new ChatResponse(conversation.Id, assistantMessage, toolCalls));
});

var conversationsEndpoint = api.MapGet("/conversations", async (
    IConversationStore conversationStore,
    CustomerContext customerContext,
    CancellationToken cancellationToken) =>
{
    var customerId = customerContext.GetCustomerId();
    if (string.IsNullOrWhiteSpace(customerId))
    {
        return Results.Unauthorized();
    }

    var conversations = await conversationStore.GetConversationsByCustomerAsync(customerId, cancellationToken);
    return Results.Ok(conversations);
});

var conversationEndpoint = api.MapGet("/conversations/{id}", async (
    string id,
    IConversationStore conversationStore,
    CustomerContext customerContext,
    CancellationToken cancellationToken) =>
{
    var customerId = customerContext.GetCustomerId();
    if (string.IsNullOrWhiteSpace(customerId))
    {
        return Results.Unauthorized();
    }

    var conversation = await conversationStore.GetConversationAsync(id, cancellationToken);
    if (conversation is null || !string.Equals(conversation.CustomerId, customerId, StringComparison.OrdinalIgnoreCase))
    {
        return Results.NotFound();
    }

    return Results.Ok(conversation);
});

var imageEndpoint = api.MapGet("/images/{filename}", async (
    string filename,
    IImageService imageService,
    CancellationToken cancellationToken) =>
{
    var image = await imageService.GetImageAsync(filename, cancellationToken);
    return image is null
        ? Results.NotFound()
        : Results.Stream(image.Value.content, image.Value.contentType);
});

var customerEndpoint = api.MapGet("/customers/{id}", async (
    string id,
    CrmApiClient crmApiClient,
    CancellationToken cancellationToken) =>
{
    using var response = await crmApiClient.GetCustomerAsync(id, cancellationToken);
    return await ProxyResponseAsync(response, cancellationToken);
});

var ordersEndpoint = api.MapGet("/customers/{id}/orders", async (
    string id,
    CrmApiClient crmApiClient,
    CancellationToken cancellationToken) =>
{
    using var response = await crmApiClient.GetCustomerOrdersAsync(id, cancellationToken);
    return await ProxyResponseAsync(response, cancellationToken);
});

if (!useInMemory)
{
    chatEndpoint.RequireAuthorization();
    conversationsEndpoint.RequireAuthorization();
    conversationEndpoint.RequireAuthorization();
    customerEndpoint.RequireAuthorization();
    ordersEndpoint.RequireAuthorization();
}

app.Run();

static async Task<IResult> ProxyResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
{
    var payload = await response.Content.ReadAsStringAsync(cancellationToken);
    var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/json";
    return Results.Content(payload, contentType, statusCode: (int)response.StatusCode);
}

internal sealed record AgentChatResponse(string Response, IReadOnlyList<ToolCallInfo> ToolCalls);

internal sealed class CosmosHealthCheck(CosmosClient client) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _ = await client.ReadAccountAsync();
            return HealthCheckResult.Healthy("Cosmos DB agents account is reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Cosmos DB agents account is not reachable.", ex);
        }
    }
}

internal sealed class CrmApiHealthCheck(CrmApiClient crmApiClient) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await crmApiClient.GetHealthAsync(cancellationToken);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy("CRM API is reachable.")
                : HealthCheckResult.Unhealthy("CRM API returned an error status.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("CRM API is not reachable.", ex);
        }
    }
}

internal sealed class OrchestratorHealthCheck(OrchestratorClient orchestratorClient) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await orchestratorClient.GetHealthAsync(cancellationToken);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy("Orchestrator Agent is reachable.")
                : HealthCheckResult.Unhealthy("Orchestrator Agent returned an error status.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Orchestrator Agent is not reachable.", ex);
        }
    }
}

public partial class Program
{
    internal static string GetConfigOrDefault(IConfiguration config, string key, string defaultValue)
        => config[key] is { Length: > 0 } value ? value : defaultValue;
}
