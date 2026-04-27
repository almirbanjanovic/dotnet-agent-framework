using Contoso.KnowledgeMcp.Services;
using Contoso.KnowledgeMcp.Tools;
using Contoso.ServiceDefaults;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ModelContextProtocol.Server;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile(
    $"appsettings.{builder.Environment.EnvironmentName}.json",
    optional: true,
    reloadOnChange: true);

builder.AddServiceDefaults();

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(5003);
});

if (string.Equals(builder.Configuration["DataMode"], "InMemory", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<ISearchService, InMemorySearchService>();
}
else
{
    builder.Services.AddSingleton<ISearchService, AzureSearchService>();
}

builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.Stateless = true)
    .WithTools<KnowledgeTools>();

builder.Services.AddHealthChecks()
    .AddCheck<SearchServiceHealthCheck>("knowledge-search", tags: ["ready"]);

var app = builder.Build();

app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = _ => false
});

app.MapHealthChecks("/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.MapMcp();

app.Run();

internal sealed class SearchServiceHealthCheck(ISearchService searchService) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _ = await searchService.SearchAsync("return policy", 1, cancellationToken);
            return HealthCheckResult.Healthy("Knowledge search is reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Knowledge search is not reachable.", ex);
        }
    }
}
