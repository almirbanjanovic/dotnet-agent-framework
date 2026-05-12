using System.ComponentModel;
using System.Text;
using Contoso.KnowledgeMcp.Services;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Contoso.KnowledgeMcp.Tools;

[McpServerToolType]
public sealed class KnowledgeTools
{
    private readonly ISearchService _searchService;

    public KnowledgeTools(ISearchService searchService)
    {
        _searchService = searchService;
    }

    [McpServerTool(Name = "search_knowledge_base", ReadOnly = true), Description(
        "Semantic search over Contoso Outdoors policies (return, refund, warranty, price-match, loyalty), " +
        "guides (backpack fitting, boot sizing, gear care, layering, tent selection), and procedures " +
        "(processing returns, exchanges, warranty claims). Returns up to topK ranked passages with their " +
        "source filename and similarity score. Do NOT re-call this tool with a rephrased version of the " +
        "same query — if the first result is off-topic, tell the customer the policy isn't covered. For " +
        "multi-topic questions (e.g. 'return AND warranty policy') use a single combined query and raise " +
        "topK (up to 10) instead of issuing multiple calls.")]
    public async Task<string> SearchKnowledgeBaseAsync(
        [Description("Natural language query to search.")] string query,
        [Description("Maximum number of passages to return (default 3, max 10).")] int topK = 3)
    {
        try
        {
            var results = await _searchService.SearchAsync(query, topK);
            if (results.Count == 0)
            {
                return "No matching knowledge base entries found.";
            }

            var builder = new StringBuilder();
            foreach (var result in results)
            {
                var snippet = result.Text.Length > 320
                    ? $"{result.Text[..320]}..."
                    : result.Text;

                builder.AppendLine($"Source: {result.Source}");
                builder.AppendLine($"Score: {result.Score:F3}");
                builder.AppendLine(snippet);
                builder.AppendLine();
            }

            return builder.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            throw new McpException($"Failed to search knowledge base. {ex.Message}", ex);
        }
    }
}
