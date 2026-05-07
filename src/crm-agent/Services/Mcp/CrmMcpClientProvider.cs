using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

// CRM MCP server client (orders, customers, support tickets).
// `CrmMcp:BaseUrl` comes from `appsettings.{Local,Development}.json`,
// or defaults to localhost for `dotnet run` outside Aspire.

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
