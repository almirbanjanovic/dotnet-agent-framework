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

    [McpServerTool(Name = "get_all_customers", ReadOnly = true), Description("List all customers.")]
    public async Task<string> GetAllCustomersAsync()
    {
        try
        {
            var customers = await _crmApiClient.GetAllCustomersAsync();
            return ToolJsonSerializer.Serialize(customers);
        }
        catch (Exception ex)
        {
            throw new McpException($"Failed to list customers. {ex.Message}", ex);
        }
    }

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
