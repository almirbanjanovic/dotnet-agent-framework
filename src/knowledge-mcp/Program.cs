using Contoso.KnowledgeMcp;
using Contoso.KnowledgeMcp.HealthChecks;
using Contoso.KnowledgeMcp.Services;
using Contoso.KnowledgeMcp.Tools;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

// ─────────────────────────────────────────────────────────────────────────
// Knowledge MCP server — exposes Contoso SharePoint/Azure Search docs as
// MCP tools.
//
// Composition root only. Concrete logic lives in:
//   Models/         → search-result DTOs
//   Services/       → ISearchService (in-memory + Azure AI Search impls),
//                     embedding helpers
//   Tools/          → KnowledgeTools (the MCP tool methods)
//   HealthChecks/   → /ready probe for the configured ISearchService
// ─────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile(
    $"appsettings.{builder.Environment.EnvironmentName}.json",
    optional: true,
    reloadOnChange: true);

builder.AddServiceDefaults();

// DataMode=InMemory swaps in a local fake so devs can run the agents
// against canned docs without provisioning Azure AI Search.
if (string.Equals(builder.Configuration["DataMode"], "InMemory", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<ISearchService, InMemorySearchService>();
}
else
{
    builder.Services.AddSingleton<ISearchService, AzureSearchService>();
}

// Pre-warm the search backend during startup. Critical for the in-memory
// implementation: without it, the first `search_knowledge_base` tool call
// has to embed every chunk on the request thread (~30-60 s in practice),
// which exceeds the agent framework's tool-call timeout and causes the
// MCP backend to be retried in a loop. No-op for `AzureSearchService`.
builder.Services.AddHostedService<SearchServiceWarmupHostedService>();

builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.Stateless = true)
    .WithTools<KnowledgeTools>();

builder.Services.AddHealthChecks()
    .AddCheck<SearchServiceHealthCheck>("knowledge-search", tags: ["ready"]);

var app = builder.Build();

app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

app.MapMcp();

app.Run();

// Make Program accessible for integration tests.
public partial class Program { }
