using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

// CRM MCP server client. The Product Agent uses CRM tools sparingly —
// e.g. to look up the customer's loyalty tier so it can recommend
// promotions they're eligible for.

internal sealed class CrmMcpClientProvider : McpClientProvider
{
    public CrmMcpClientProvider(IConfiguration configuration, ILoggerFactory loggerFactory)
        : base("crm-mcp", configuration["CrmMcp:BaseUrl"] ?? "http://localhost:5002", loggerFactory)
    {
    }
}
