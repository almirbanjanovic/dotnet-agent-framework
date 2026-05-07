using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

// Knowledge MCP server client (semantic search over the SharePoint
// content set). The CRM Agent uses this to answer policy/procedure
// questions like "what's the return window for boots?".

internal sealed class KnowledgeMcpClientProvider : McpClientProvider
{
    public KnowledgeMcpClientProvider(
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILoggerFactory loggerFactory)
        : base(
            "knowledge-mcp",
            configuration["KnowledgeMcp:BaseUrl"] ?? "http://localhost:5003",
            httpClientFactory,
            loggerFactory)
    {
    }
}
