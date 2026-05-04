using Contoso.ProductAgent;
using Contoso.ProductAgent.Endpoints;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

// ─────────────────────────────────────────────────────────────────────────
// Product Agent — specialist for catalog, recommendations, promotions.
//
// Composition root only. All concrete logic lives in:
//   Models/         → ChatRequest / ChatResponse wire records
//   Services/       → Agent factory, MCP client cache, prompt loader,
//                     chat-history binder, tool-call extractor
//   Services/Mcp/   → CRM and Knowledge MCP client providers
//   HealthChecks/   → /ready probes for downstream MCP and Foundry
//   Endpoints/      → POST /api/v1/chat
// ─────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile(
    $"appsettings.{builder.Environment.EnvironmentName}.json",
    optional: true,
    reloadOnChange: true);

builder.AddServiceDefaults();

builder.Services.AddSingleton<CrmMcpClientProvider>();
builder.Services.AddSingleton<KnowledgeMcpClientProvider>();
builder.Services.AddSingleton<SystemPromptProvider>();
builder.Services.AddSingleton<ProductAgentFactory>();

builder.Services.AddHealthChecks()
    .AddCheck<CrmMcpHealthCheck>("crm-mcp", tags: ["ready"])
    .AddCheck<KnowledgeMcpHealthCheck>("knowledge-mcp", tags: ["ready"])
    .AddCheck<FoundryHealthCheck>("foundry", tags: ["ready"]);

var app = builder.Build();

// /health is the liveness probe (always 200 if the process is up).
// /ready is the readiness probe (only 200 when downstream deps are reachable).
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

app.MapChatEndpoint();

app.Run();

// Make Program accessible for integration tests.
public partial class Program { }
