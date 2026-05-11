using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Contoso.FraudWorkflow.Services.Mcp;

internal sealed class CrmMcpClientProvider : McpClientProvider
{
    public CrmMcpClientProvider(
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILoggerFactory loggerFactory)
        : base(
            "crm-mcp",
            configuration["CrmMcp:BaseUrl"] ?? "http://localhost:5002",
            httpClientFactory,
            loggerFactory)
    {
    }
}
