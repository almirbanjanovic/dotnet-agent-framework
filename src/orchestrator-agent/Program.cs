using Contoso.OrchestratorAgent;
using Contoso.OrchestratorAgent.Models;
using Contoso.OrchestratorAgent.Services;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile(
    $"appsettings.{builder.Environment.EnvironmentName}.json",
    optional: true,
    reloadOnChange: true);

builder.AddServiceDefaults();

// Port binding is driven by ASPNETCORE_URLS (Aspire), appsettings, or the
// container platform — not hardcoded.

builder.Services.AddSingleton<SystemPromptProvider>();
builder.Services.AddSingleton<IntentClassifier>();
builder.Services.AddSingleton<AgentRouter>();

// Forward customer identity to specialist agents.
builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<CustomerHeaderForwarder>();

builder.Services.AddHttpClient<CrmAgentClient>(client =>
    {
        var baseUrl = builder.Configuration["CrmAgent:BaseUrl"] ?? "http://localhost:5004";
        client.BaseAddress = new Uri(baseUrl);
    })
    .AddHttpMessageHandler<CustomerHeaderForwarder>()
    .AddStandardResilienceHandler();

builder.Services.AddHttpClient<ProductAgentClient>(client =>
    {
        var baseUrl = builder.Configuration["ProductAgent:BaseUrl"] ?? "http://localhost:5005";
        client.BaseAddress = new Uri(baseUrl);
    })
    .AddHttpMessageHandler<CustomerHeaderForwarder>()
    .AddStandardResilienceHandler();

builder.Services.AddHealthChecks()
    .AddCheck<CrmAgentHealthCheck>("crm-agent", tags: ["ready"])
    .AddCheck<ProductAgentHealthCheck>("product-agent", tags: ["ready"])
    .AddCheck<FoundryHealthCheck>("foundry", tags: ["ready"]);

var app = builder.Build();

app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = _ => false
});

app.MapHealthChecks("/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.MapPost("/api/v1/chat", async (
    ChatRequest request,
    IntentClassifier classifier,
    AgentRouter router,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.CustomerId) || string.IsNullOrWhiteSpace(request.Message))
    {
        return Results.BadRequest(new { error = "customerId and message are required." });
    }

    var intent = await classifier.ClassifyAsync(request.Message, cancellationToken);
    var result = await router.RouteAsync(intent, request, cancellationToken);

    if (string.IsNullOrWhiteSpace(result.Payload))
    {
        return Results.StatusCode(result.StatusCode);
    }

    return Results.Content(result.Payload, "application/json", statusCode: result.StatusCode);
});

app.Run();

internal sealed class CrmAgentHealthCheck(CrmAgentClient client) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await client.HttpClient.GetAsync("/health", cancellationToken);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy("CRM Agent is reachable.")
                : HealthCheckResult.Unhealthy("CRM Agent returned an error status.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("CRM Agent is not reachable.", ex);
        }
    }
}

internal sealed class ProductAgentHealthCheck(ProductAgentClient client) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await client.HttpClient.GetAsync("/health", cancellationToken);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy("Product Agent is reachable.")
                : HealthCheckResult.Unhealthy("Product Agent returned an error status.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Product Agent is not reachable.", ex);
        }
    }
}

internal sealed class FoundryHealthCheck(IntentClassifier classifier) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _ = await classifier.ClassifyAsync("Ping", cancellationToken);
            return HealthCheckResult.Healthy("Foundry chat model is reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Foundry chat model is not reachable.", ex);
        }
    }
}

// Make Program accessible for integration tests
public partial class Program { }
