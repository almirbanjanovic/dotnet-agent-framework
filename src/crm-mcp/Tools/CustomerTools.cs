using System.ComponentModel;
using Contoso.CrmMcp.Clients;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Contoso.CrmMcp.Tools;

[McpServerToolType]
public sealed class CustomerTools
{
    private readonly CrmApiClient _crmApiClient;

    public CustomerTools(CrmApiClient crmApiClient)
    {
        _crmApiClient = crmApiClient;
    }

    // NOTE: a `get_all_customers` tool used to live here. It was removed
    // because (a) no agent in this repo has a legitimate need to enumerate
    // every customer and (b) exposing such a tool to an LLM is a textbook
    // exfiltration vector — a prompt-injection payload could trigger a
    // dump of the entire customer table. If a back-office / admin surface
    // ever needs bulk listing, build it as a separate, role-gated tool
    // (and a separate REST endpoint behind real authorization), not as a
    // tool any chat session can invoke.

    [McpServerTool(Name = "get_customer_detail", ReadOnly = true), Description("Get a customer by ID.")]
    public async Task<string> GetCustomerDetailAsync(
        [Description("Customer ID.")] string id)
    {
        try
        {
            var customer = await _crmApiClient.GetCustomerByIdAsync(id);
            return ToolJsonSerializer.Serialize(customer);
        }
        catch (Exception ex)
        {
            throw new McpException($"Failed to get customer '{id}'. {ex.Message}", ex);
        }
    }
}
