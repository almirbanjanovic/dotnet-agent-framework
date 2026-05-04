using Contoso.OrchestratorAgent;
using Contoso.OrchestratorAgent.Endpoints;
using Contoso.OrchestratorAgent.HealthChecks;
using Contoso.OrchestratorAgent.Services;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

// ─────────────────────────────────────────────────────────────────────────
// Orchestrator Agent — front door for the BFF. Picks the specialist
// agent (CRM vs Product) and proxies the chat turn to it.
//
// Composition root only. Concrete logic lives in:
//   Models/         → ChatRequest / ChatResponse wire records
//   Services/       → IntentClassifier, AgentRouter, the typed
//                     CrmAgentClient / ProductAgentClient HTTP wrappers,
//                     CustomerHeaderForwarder, SystemPromptProvider
//   HealthChecks/   → /ready probes for both specialist agents and Foundry
//   Endpoints/      → POST /api/v1/chat
// ─────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile(
    $"appsettings.{builder.Environment.EnvironmentName}.json",
    optional: true,
    reloadOnChange: true);

builder.AddServiceDefaults();

builder.Services.AddSingleton<SystemPromptProvider>();
builder.Services.AddSingleton<IntentClassifier>();
builder.Services.AddSingleton<AgentRouter>();

// Forward the X-Customer-Id header onto outbound calls so the specialist
// agents know which customer the request belongs to.
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

app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

app.MapChatEndpoint();

app.Run();

// Make Program accessible for integration tests.
public partial class Program { }
