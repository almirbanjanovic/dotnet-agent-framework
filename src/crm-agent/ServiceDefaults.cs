using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Contoso.CrmAgent;

// Per-service hosting defaults (OpenTelemetry, service discovery, HTTP resilience,
// default health checks). Inlined per service so each component remains fully
// self-contained — no shared project references.
internal static class ServiceDefaults
{
    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();

        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"]);

        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Agent calls (Foundry chat + MCP tool round-trips) routinely take 30+ seconds.
            // Polly's defaults (10 s per attempt, 30 s total) kill them before they finish, so
            // we widen them here. Every typed client in this service inherits these settings;
            // do NOT chain a second .AddStandardResilienceHandler() in Program.cs or you will
            // stack a second pipeline with the unconfigured defaults.
            http.AddStandardResilienceHandler(options =>
            {
                options.AttemptTimeout.Timeout = TimeSpan.FromMinutes(2);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(5);
                // Polly validation: SamplingDuration must be >= 2 * AttemptTimeout.
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(4);
                // Retrying an agent call burns tokens and may duplicate side effects.
                // One retry is enough to absorb transient transport blips.
                options.Retry.MaxRetryAttempts = 1;
            });
            http.AddServiceDiscovery();
        });

        return builder;
    }

    private static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
            })
            .WithTracing(tracing =>
            {
                tracing.AddSource(builder.Environment.ApplicationName)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation(o =>
                    {
                        // The MCP streamable-HTTP client opens an optional
                        // server-to-client SSE listen stream by sending
                        // `GET / Accept: text/event-stream` after every
                        // session initialize. Stateless servers respond 405
                        // (per spec) and the client moves on. Suppress these
                        // probe spans so traces aren't littered with red
                        // 405s that aren't real failures.
                        o.FilterHttpRequestMessage = req =>
                            !(req.Method == HttpMethod.Get
                                && req.Headers.Accept.Any(h => h.MediaType == "text/event-stream"));
                    });
            });

        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        return builder;
    }
}
