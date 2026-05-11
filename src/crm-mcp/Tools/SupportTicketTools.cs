using System.ComponentModel;
using Contoso.CrmMcp.Clients;
using Contoso.CrmMcp.Models;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Contoso.CrmMcp.Tools;

[McpServerToolType]
public sealed class SupportTicketTools
{
    private readonly CrmApiClient _crmApiClient;

    public SupportTicketTools(CrmApiClient crmApiClient)
    {
        _crmApiClient = crmApiClient;
    }

    [McpServerTool(Name = "get_support_tickets", ReadOnly = true), Description("Get support tickets for a customer.")]
    public async Task<string> GetSupportTicketsAsync(
        [Description("Customer ID.")] string customerId,
        [Description("Return only open tickets.")] bool openOnly = false)
    {
        try
        {
            var tickets = await _crmApiClient.GetTicketsByCustomerIdAsync(customerId, openOnly);
            return ToolJsonSerializer.Serialize(tickets);
        }
        catch (Exception ex)
        {
            throw new McpException($"Failed to get support tickets for '{customerId}'. {ex.Message}", ex);
        }
    }

    [McpServerTool(Name = "create_support_ticket", ReadOnly = false), Description("Create a new support ticket. Use this to start a return/refund (category='return'), report a shipping problem (category='shipping'), or escalate a defective product (category='product-issue'). All enum fields are case-sensitive lowercase.")]
    public async Task<string> CreateSupportTicketAsync(
        [Description("Support ticket request payload. Note category and priority are case-sensitive lowercase enums — see field descriptions for allowed values.")] CreateTicketRequest request)
    {
        try
        {
            var ticket = await _crmApiClient.CreateTicketAsync(request);
            return ToolJsonSerializer.Serialize(ticket);
        }
        catch (Exception ex)
        {
            throw new McpException($"Failed to create support ticket. {ex.Message}", ex);
        }
    }
}
