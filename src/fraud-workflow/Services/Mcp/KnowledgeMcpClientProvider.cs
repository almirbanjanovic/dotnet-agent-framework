using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Contoso.FraudWorkflow.Services.Mcp;

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
