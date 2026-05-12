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

    [McpServerTool(Name = "get_support_tickets", ReadOnly = true), Description("Get support tickets for a customer. Returns ALL categories (return, product-issue, shipping, general). When the customer asks to cancel, close, withdraw, or check the status of any ticket — even if they only mention 'return' or 'refund' — call this with open_only=true and disambiguate with them; do NOT pre-filter by category.")]
    public async Task<string> GetSupportTicketsAsync(
        [Description("Customer ID.")] string customerId,
        [Description("Return only tickets with status='open'.")] bool openOnly = false)
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

    [McpServerTool(Name = "create_support_ticket", ReadOnly = false), Description("Create a new support ticket. Use this to start a return/refund (category='return'), report a shipping problem (category='shipping'), or escalate a defective product (category='product-issue'). All enum fields are case-sensitive lowercase. NOTE: when category='return' AND order_id is set, the back-end automatically opens a refund-risk review for the operations team — do not also tell the customer to email anyone. ELIGIBILITY: a return can only be filed against an order whose current status is 'delivered'. If the customer's order is still 'shipped' or 'processing', tell them they need to wait until it is delivered before a return can be filed; do NOT call this tool. If the order is already 'return-requested' or 'returned', the customer should cancel the existing return ticket first or has nothing to return. The server enforces this and will reject ineligible calls with a 409 — read the error message verbatim back to the customer.")]
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

    [McpServerTool(Name = "cancel_support_ticket", ReadOnly = false), Description("Cancel an open support ticket on the customer's behalf. Use this when the customer asks to cancel, withdraw, or 'never mind' a ticket they previously opened. Requires the exact ticket id (e.g. 'ST-001') — call get_support_tickets first to find it. Only works on tickets in status='open'; tickets already cancelled, resolved, or closed will return a 409.")]
    public async Task<string> CancelSupportTicketAsync(
        [Description("Ticket ID to cancel (e.g. 'ST-001'). Must be an EXISTING ticket id returned by get_support_tickets.")] string ticketId,
        [Description("Customer ID that owns the ticket. Used as a fallback when the per-request customer header is absent (tests/local).")] string customerId)
    {
        try
        {
            var ticket = await _crmApiClient.UpdateTicketStatusAsync(ticketId, "cancelled", customerId);
            return ToolJsonSerializer.Serialize(ticket);
        }
        catch (Exception ex)
        {
            throw new McpException($"Failed to cancel support ticket '{ticketId}'. {ex.Message}", ex);
        }
    }
}
