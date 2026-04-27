using System.ComponentModel;
using Contoso.CrmMcp.Clients;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Contoso.CrmMcp.Tools;

[McpServerToolType]
public sealed class ProductTools
{
    private readonly CrmApiClient _crmApiClient;

    public ProductTools(CrmApiClient crmApiClient)
    {
        _crmApiClient = crmApiClient;
    }

    [McpServerTool(Name = "get_products", ReadOnly = true), Description("Search or list products.")]
    public async Task<string> GetProductsAsync(
        [Description("Search query text.")] string? query = null,
        [Description("Product category filter.")] string? category = null,
        [Description("Return only products that are in stock.")] bool? inStockOnly = null)
    {
        try
        {
            var products = await _crmApiClient.GetProductsAsync(query, category, inStockOnly);
            return ToolJsonSerializer.Serialize(products);
        }
        catch (Exception ex)
        {
            throw new McpException($"Failed to get products. {ex.Message}", ex);
        }
    }

    [McpServerTool(Name = "get_product_detail", ReadOnly = true), Description("Get a product by ID.")]
    public async Task<string> GetProductDetailAsync(
        [Description("Product ID.")] string id)
    {
        try
        {
            var product = await _crmApiClient.GetProductByIdAsync(id);
            return ToolJsonSerializer.Serialize(product);
        }
        catch (Exception ex)
        {
            throw new McpException($"Failed to get product '{id}'. {ex.Message}", ex);
        }
    }
}
