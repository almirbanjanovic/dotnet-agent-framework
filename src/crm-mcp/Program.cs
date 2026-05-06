using Contoso.CrmMcp;
using Contoso.CrmMcp.Clients;
using Contoso.CrmMcp.HealthChecks;
using Contoso.CrmMcp.Tools;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

// ─────────────────────────────────────────────────────────────────────────
// CRM MCP server — exposes Contoso CRM data as MCP tools.
//
// Composition root only. Concrete logic lives in:
//   Models/         → DTOs returned by tools
//   Clients/        → Typed HttpClient for the CRM API,
//                     CustomerHeaderForwarder for identity propagation
//   Tools/          → [McpServerToolType] classes (Customer, Order,
//                     Product, Promotion, SupportTicket)
//   HealthChecks/   → /ready probe for the CRM API
// ─────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile(
    $"appsettings.{builder.Environment.EnvironmentName}.json",
    optional: true,
    reloadOnChange: true);

builder.AddServiceDefaults();

var crmApiBaseUrl = builder.Configuration["CrmApi:BaseUrl"]
    ?? throw new InvalidOperationException("CrmApi:BaseUrl configuration is required.");

// Forward customer identity from inbound MCP request to outbound CRM API call.
builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<CustomerHeaderForwarder>();

// The standard resilience handler is added once for all clients in
// ServiceDefaults.ConfigureHttpClientDefaults; do not add it again here.
builder.Services.AddHttpClient<CrmApiClient>(client => client.BaseAddress = new Uri(crmApiBaseUrl))
    .AddHttpMessageHandler<CustomerHeaderForwarder>();

builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.Stateless = true)
    .WithTools<CustomerTools>()
    .WithTools<OrderTools>()
    .WithTools<ProductTools>()
    .WithTools<PromotionTools>()
    .WithTools<SupportTicketTools>();

builder.Services.AddHealthChecks()
    .AddCheck<CrmApiHealthCheck>("crm-api", tags: ["ready"]);

var app = builder.Build();

app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

app.MapMcp();

app.Run();

// Make Program accessible for integration tests.
public partial class Program { }
