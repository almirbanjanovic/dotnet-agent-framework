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

    [McpServerTool(Name = "create_support_ticket", ReadOnly = false), Description("Create a support ticket. category='return' for refunds, 'shipping' for delivery problems, 'product-issue' for defects. All enums lowercase. Returns: order flips to 'return-started' and a prepaid return label is auto-issued (visible as return_label_id/return_label_carrier). RETURN ELIGIBILITY (server enforces with 409 — read the error message verbatim): order status must be 'delivered' AND the delivery date must be within the 30-day return window. Do not call for 'shipped', 'processing', 'cancelled', 'return-started', or 'returned' orders, or for orders delivered more than 30 days ago.")]
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

    [McpServerTool(Name = "cancel_support_ticket", ReadOnly = false), Description("Cancel an open support ticket on the customer's behalf. Use this when the customer asks to cancel, withdraw, or 'never mind' a ticket they previously opened. Requires the exact ticket id (e.g. 'ST-001') — call get_support_tickets first to find it. Only works on tickets in status='open'; tickets already cancelled, resolved, or closed will return a 409. For return tickets, cancelling automatically voids the prepaid return shipping label — tell the customer not to use it.")]
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
