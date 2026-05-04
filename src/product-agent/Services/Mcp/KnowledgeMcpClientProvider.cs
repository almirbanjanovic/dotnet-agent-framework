using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

// Knowledge MCP server client. Backbone of the Product Agent — it answers
// most product questions ("which tent is best for 3 people in summer?")
// from the SharePoint guides indexed there.

internal sealed class KnowledgeMcpClientProvider : McpClientProvider
{
    public KnowledgeMcpClientProvider(IConfiguration configuration, ILoggerFactory loggerFactory)
        : base("knowledge-mcp", configuration["KnowledgeMcp:BaseUrl"] ?? "http://localhost:5003", loggerFactory)
    {
    }
}
