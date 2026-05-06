using Contoso.CrmAgent;
using Contoso.CrmAgent.Endpoints;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

// ─────────────────────────────────────────────────────────────────────────
// CRM Agent — specialist for orders, returns, support tickets, account.
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

// Singletons — all stateless or internally synchronized. Port binding is
// driven by ASPNETCORE_URLS (Aspire), appsettings, or the container
// platform — never hardcoded here.
builder.Services.AddSingleton<CrmMcpClientProvider>();
builder.Services.AddSingleton<KnowledgeMcpClientProvider>();
builder.Services.AddSingleton<SystemPromptProvider>();
builder.Services.AddSingleton<CrmAgentFactory>();

builder.Services.AddHealthChecks()
    .AddCheck<CrmMcpHealthCheck>("crm-mcp", tags: ["ready"])
    .AddCheck<KnowledgeMcpHealthCheck>("knowledge-mcp", tags: ["ready"])
    .AddCheck<FoundryHealthCheck>("foundry", tags: ["ready"]);

var app = builder.Build();

// Surface unhandled exceptions as JSON so callers (orchestrator / browser) get
// the actual error message instead of an empty 500.
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var feature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
        var ex = feature?.Error;
        var logger = context.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Contoso.CrmAgent.UnhandledException");
        logger.LogError(ex, "Unhandled exception on {Path}", context.Request.Path);

        // Do NOT leak ex.Message or stack traces to network clients — stack
        // frames disclose namespaces, file paths, and code structure that
        // accelerate exploitation. Full diagnostics live in the server log
        // tied to the trace id.
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new
        {
            error = ex?.GetType().Name ?? "InternalServerError",
            message = "An internal error occurred. See server logs for details.",
            traceId = context.TraceIdentifier,
            path = context.Request.Path.Value
        });
    });
});

// /health is the liveness probe (always 200 if the process is up).
// /ready is the readiness probe (only 200 when downstream deps are reachable).
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

app.MapChatEndpoint();

app.Run();

// Make Program accessible for integration tests.
public partial class Program { }
