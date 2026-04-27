using System.ComponentModel;
using Contoso.CrmMcp.Clients;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Contoso.CrmMcp.Tools;

[McpServerToolType]
public sealed class PromotionTools
{
    private readonly CrmApiClient _crmApiClient;

    public PromotionTools(CrmApiClient crmApiClient)
    {
        _crmApiClient = crmApiClient;
    }

    [McpServerTool(Name = "get_promotions", ReadOnly = true), Description("List all active promotions.")]
    public async Task<string> GetPromotionsAsync()
    {
        try
        {
            var promotions = await _crmApiClient.GetAllPromotionsAsync();
            return ToolJsonSerializer.Serialize(promotions);
        }
        catch (Exception ex)
        {
            throw new McpException($"Failed to list promotions. {ex.Message}", ex);
        }
    }

    [McpServerTool(Name = "get_eligible_promotions", ReadOnly = true), Description("Get promotions eligible for a customer.")]
    public async Task<string> GetEligiblePromotionsAsync(
        [Description("Customer ID.")] string customerId)
    {
        try
        {
            var promotions = await _crmApiClient.GetEligiblePromotionsAsync(customerId);
            return ToolJsonSerializer.Serialize(promotions);
        }
        catch (Exception ex)
        {
            throw new McpException($"Failed to get eligible promotions for '{customerId}'. {ex.Message}", ex);
        }
    }
}
