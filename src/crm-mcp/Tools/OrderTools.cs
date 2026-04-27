using System.ComponentModel;
using Contoso.CrmMcp.Clients;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Contoso.CrmMcp.Tools;

[McpServerToolType]
public sealed class OrderTools
{
    private readonly CrmApiClient _crmApiClient;

    public OrderTools(CrmApiClient crmApiClient)
    {
        _crmApiClient = crmApiClient;
    }

    [McpServerTool(Name = "get_customer_orders", ReadOnly = true), Description("Get all orders for a customer.")]
    public async Task<string> GetCustomerOrdersAsync(
        [Description("Customer ID.")] string customerId)
    {
        try
        {
            var orders = await _crmApiClient.GetOrdersByCustomerIdAsync(customerId);
            return ToolJsonSerializer.Serialize(orders);
        }
        catch (Exception ex)
        {
            throw new McpException($"Failed to get orders for customer '{customerId}'. {ex.Message}", ex);
        }
    }

    [McpServerTool(Name = "get_order_detail", ReadOnly = true), Description("Get an order by ID.")]
    public async Task<string> GetOrderDetailAsync(
        [Description("Order ID.")] string id)
    {
        try
        {
            var order = await _crmApiClient.GetOrderByIdAsync(id);
            return ToolJsonSerializer.Serialize(order);
        }
        catch (Exception ex)
        {
            throw new McpException($"Failed to get order '{id}'. {ex.Message}", ex);
        }
    }

    [McpServerTool(Name = "get_order_items", ReadOnly = true), Description("Get line items for an order.")]
    public async Task<string> GetOrderItemsAsync(
        [Description("Order ID.")] string orderId)
    {
        try
        {
            var items = await _crmApiClient.GetOrderItemsByOrderIdAsync(orderId);
            return ToolJsonSerializer.Serialize(items);
        }
        catch (Exception ex)
        {
            throw new McpException($"Failed to get order items for '{orderId}'. {ex.Message}", ex);
        }
    }
}
