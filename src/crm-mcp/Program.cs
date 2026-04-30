using Contoso.CrmMcp.Clients;
using Contoso.CrmMcp.Tools;
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

// Port binding is driven by ASPNETCORE_URLS (Aspire), appsettings, or the
// container platform — not hardcoded.

var crmApiBaseUrl = builder.Configuration["CrmApi:BaseUrl"]
    ?? throw new InvalidOperationException("CrmApi:BaseUrl configuration is required.");

// Forward customer identity from inbound MCP request to outbound CRM API call.
builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<CustomerHeaderForwarder>();

builder.Services.AddHttpClient<CrmApiClient>(client =>
{
    client.BaseAddress = new Uri(crmApiBaseUrl);
})
.AddHttpMessageHandler<CustomerHeaderForwarder>()
.AddStandardResilienceHandler();

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

internal sealed class CrmApiHealthCheck(CrmApiClient crmApiClient) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _ = await crmApiClient.GetAllCustomersAsync(cancellationToken);
            return HealthCheckResult.Healthy("CRM API is reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("CRM API is not reachable.", ex);
        }
    }
}
