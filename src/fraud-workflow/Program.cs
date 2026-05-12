using Contoso.FraudWorkflow;
using Contoso.FraudWorkflow.Agents;
using Contoso.FraudWorkflow.Endpoints;
using Contoso.FraudWorkflow.HealthChecks;
using Contoso.FraudWorkflow.Services;
using Contoso.FraudWorkflow.Services.Mcp;
using Contoso.FraudWorkflow.Workflows;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Text.Json.Serialization;

// ─────────────────────────────────────────────────────────────────────────
// Fraud Workflow — refund risk fan-out / fan-in / human-in-the-loop.
//
// Composition root only. All concrete logic lives in:
//   Models/         → RefundAlert, AgentFinding, RefundRiskAssessment,
//                     ApprovalDecision, FinalAction
//   Services/       → FraudAgentFactory, RiskAggregator, IApprovalGate
//                     + InMemoryApprovalGate
//   Services/Mcp/   → CRM and Knowledge MCP client providers
//   Agents/         → OrderHistoryAgent, ReturnConditionAgent,
//                     LoyaltyContextAgent (one specialist each)
//   Workflows/      → RouterExecutor, AgentExecutors, AggregatorExecutor,
//                     HumanGateExecutor, RefundRiskWorkflow,
//                     FraudWorkflowRunner
//   Endpoints/      → POST /api/v1/refunds, GET/POST /api/v1/operations/*
//   HealthChecks/   → /ready probes for downstream MCP backends
// ─────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile(
    $"appsettings.{builder.Environment.EnvironmentName}.json",
    optional: true,
    reloadOnChange: true);

builder.AddServiceDefaults();

builder.Services.AddHttpContextAccessor();

// Accept enum values as strings on the wire (e.g. "Approve") instead of
// the System.Text.Json default of integers. The BFF and Blazor UI both
// post `{ "decision": "Approve" }`, so without this converter the
// Operations dashboard would 400 every decision submission.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

// Named HttpClients for each MCP backend (matches the crm-agent pattern).
builder.Services.AddHttpClient("crm-mcp");
builder.Services.AddHttpClient("knowledge-mcp");

builder.Services.AddSingleton<CrmMcpClientProvider>();
builder.Services.AddSingleton<KnowledgeMcpClientProvider>();

// One Foundry-backed AIAgent per specialist (created lazily inside each
// agent class via FraudAgentFactory.CreateAgent).
builder.Services.AddSingleton<FraudAgentFactory>();
builder.Services.AddSingleton<OrderHistoryAgent>();
builder.Services.AddSingleton<ReturnConditionAgent>();
builder.Services.AddSingleton<LoyaltyContextAgent>();

builder.Services.AddSingleton<RiskAggregator>();
builder.Services.AddSingleton<IApprovalGate, InMemoryApprovalGate>();
builder.Services.AddSingleton<FraudWorkflowRunner>();

// Outbound callback to crm-api so the customer-facing ticket reflects
// the workflow's terminal decision (auto-approve, operator decision,
// timeout). Service-to-service — no auth surface required because the
// CRM API trusts cluster-network callers and the /internal/ path is
// not reverse-proxied by the BFF.
builder.Services.AddHttpClient<CrmApiClient>(client =>
{
    var baseUrl = builder.Configuration["services:crm-api:http:0"]
        ?? builder.Configuration["CrmApi:BaseUrl"]
        ?? "http://localhost:5001";
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(5);
});

builder.Services.AddHealthChecks()
    .AddCheck<CrmMcpHealthCheck>("crm-mcp", tags: ["ready"])
    .AddCheck<KnowledgeMcpHealthCheck>("knowledge-mcp", tags: ["ready"]);

var app = builder.Build();

// Surface unhandled exceptions as a sanitized JSON error response. Same
// shape as the other Contoso services so the BFF can parse it uniformly.
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var feature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
        var ex = feature?.Error;
        var logger = context.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Contoso.FraudWorkflow.UnhandledException");
        logger.LogError(ex, "Unhandled exception on {Path}", context.Request.Path);

        // Do NOT leak ex.Message or stack frames — they routinely contain
        // payload fragments and code structure that accelerate exploitation.
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

// Eager-instantiate the runner so the underlying Workflow is built at
// startup. Without this the first /refunds POST pays a one-time cost
// (and surfaces any wiring errors to a customer instead of an operator).
_ = app.Services.GetRequiredService<FraudWorkflowRunner>();

app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

app.MapRefundEndpoint();
app.MapOperationsEndpoint();

app.Run();

// Make Program accessible for integration tests.
public partial class Program { }
