using Contoso.CrmApi.Models;
using Contoso.CrmApi.Services;
using FluentAssertions;

namespace Contoso.CrmApi.Tests;

public class InMemoryCrmDataServiceTests
{
    private static InMemoryCrmDataService CreateService() =>
        new(TestDataHelper.BuildConfiguration());

    [Fact]
    public async Task GetAllCustomersAsync_WithLoadedData_ReturnsAllCsvCustomers()
    {
        var service = CreateService();

        var customers = await service.GetAllCustomersAsync();

        customers.Should().HaveCount(TestDataHelper.CountCsvRows("customers.csv"));
    }

    [Fact]
    public async Task GetCustomerByIdAsync_ValidId_ReturnsCustomer()
    {
        var service = CreateService();

        var customer = await service.GetCustomerByIdAsync("101");

        customer.Should().NotBeNull();
        customer!.FirstName.Should().Be("Emma");
        customer.LastName.Should().Be("Wilson");
    }

    [Fact]
    public async Task GetCustomerByIdAsync_MissingId_ReturnsNull()
    {
        var service = CreateService();

        var customer = await service.GetCustomerByIdAsync("9999");

        customer.Should().BeNull();
    }

    [Fact]
    public async Task GetOrderByIdAsync_ValidId_ReturnsOrder()
    {
        var service = CreateService();

        var order = await service.GetOrderByIdAsync("1001");

        order.Should().NotBeNull();
        order!.CustomerId.Should().Be("101");
        order.Status.Should().Be("shipped");
    }

    [Fact]
    public async Task GetOrderByIdAsync_MissingId_ReturnsNull()
    {
        var service = CreateService();

        var order = await service.GetOrderByIdAsync("9999");

        order.Should().BeNull();
    }

    [Fact]
    public async Task GetOrdersByCustomerIdAsync_ValidCustomerId_ReturnsOrders()
    {
        var service = CreateService();
        var expectedCount = CsvDataLoader.LoadOrders(TestDataHelper.GetCsvPath("orders.csv"))
            .Count(order => order.CustomerId == "105");

        var orders = await service.GetOrdersByCustomerIdAsync("105");

        orders.Should().HaveCount(expectedCount);
        orders.Should().OnlyContain(order => order.CustomerId == "105");
    }

    [Fact]
    public async Task GetOrdersByCustomerIdAsync_UnknownCustomerId_ReturnsEmpty()
    {
        var service = CreateService();

        var orders = await service.GetOrdersByCustomerIdAsync("9999");

        orders.Should().BeEmpty();
    }

    [Fact]
    public async Task GetOrderItemsByOrderIdAsync_ValidOrderId_ReturnsItems()
    {
        var service = CreateService();
        var expectedCount = CsvDataLoader.LoadOrderItems(TestDataHelper.GetCsvPath("order-items.csv"))
            .Count(item => item.OrderId == "1005");

        var items = await service.GetOrderItemsByOrderIdAsync("1005");

        items.Should().HaveCount(expectedCount);
        items.Should().OnlyContain(item => item.OrderId == "1005");
    }

    [Fact]
    public async Task GetOrderItemsByOrderIdAsync_UnknownOrderId_ReturnsEmpty()
    {
        var service = CreateService();

        var items = await service.GetOrderItemsByOrderIdAsync("9999");

        items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetProductsAsync_NoFilters_ReturnsAll()
    {
        var service = CreateService();

        var products = await service.GetProductsAsync();

        products.Should().HaveCount(TestDataHelper.CountCsvRows("products.csv"));
    }

    [Fact]
    public async Task GetProductsAsync_CategoryFilter_ReturnsMatchingOnly()
    {
        var service = CreateService();

        var products = await service.GetProductsAsync(category: "tents");

        products.Should().NotBeEmpty();
        products.Should().OnlyContain(product =>
            string.Equals(product.Category, "tents", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetProductsAsync_InStockOnlyFilter_ReturnsInStockOnly()
    {
        var service = CreateService();

        var products = await service.GetProductsAsync(inStockOnly: true);

        products.Should().NotBeEmpty();
        products.Should().OnlyContain(product => product.InStock);
    }

    [Fact]
    public async Task GetProductsAsync_QueryFilter_MatchesNameOrDescription()
    {
        var service = CreateService();

        var products = await service.GetProductsAsync(query: "waterproof");

        products.Should().NotBeEmpty();
        products.Should().OnlyContain(product =>
            product.Name.Contains("waterproof", StringComparison.OrdinalIgnoreCase)
            || product.Description.Contains("waterproof", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetProductsAsync_CombinedFilters_ReturnsIntersection()
    {
        var service = CreateService();

        var products = await service.GetProductsAsync(query: "tent", category: "tents", inStockOnly: true);

        products.Should().NotBeEmpty();
        products.Should().OnlyContain(product =>
            product.InStock
            && string.Equals(product.Category, "tents", StringComparison.OrdinalIgnoreCase)
            && (product.Name.Contains("tent", StringComparison.OrdinalIgnoreCase)
                || product.Description.Contains("tent", StringComparison.OrdinalIgnoreCase)));
        products.Should().Contain(product => product.Id == "P005");
        products.Should().NotContain(product => product.Id == "P007");
    }

    [Fact]
    public async Task GetProductByIdAsync_ValidId_ReturnsProduct()
    {
        var service = CreateService();

        var product = await service.GetProductByIdAsync("P001");

        product.Should().NotBeNull();
        product!.Name.Should().Be("TrailBlazer Hiking Boots");
    }

    [Fact]
    public async Task GetProductByIdAsync_MissingId_ReturnsNull()
    {
        var service = CreateService();

        var product = await service.GetProductByIdAsync("PX-999");

        product.Should().BeNull();
    }

    [Fact]
    public async Task GetAllPromotionsAsync_ReturnsOnlyActivePromotions()
    {
        var service = CreateService();
        var expectedCount = CsvDataLoader.LoadPromotions(TestDataHelper.GetCsvPath("promotions.csv"))
            .Count(promo => promo.Active);

        var promotions = await service.GetAllPromotionsAsync();

        promotions.Should().HaveCount(expectedCount);
        promotions.Should().OnlyContain(promo => promo.Active);
    }

    [Fact]
    public async Task GetEligiblePromotionsAsync_BronzeTier_ReturnsBronzeOnly()
    {
        var service = CreateService();
        var expectedCount = CsvDataLoader.LoadPromotions(TestDataHelper.GetCsvPath("promotions.csv"))
            .Count(promo => promo.Active && promo.MinLoyaltyTier == "Bronze");

        var promotions = await service.GetEligiblePromotionsAsync("Bronze");

        promotions.Should().HaveCount(expectedCount);
        promotions.Should().OnlyContain(promo =>
            string.Equals(promo.MinLoyaltyTier, "Bronze", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetEligiblePromotionsAsync_GoldTier_ReturnsBronzeSilverGold()
    {
        var service = CreateService();
        var allowedTiers = new[] { "Bronze", "Silver", "Gold" };
        var expectedCount = CsvDataLoader.LoadPromotions(TestDataHelper.GetCsvPath("promotions.csv"))
            .Count(promo => promo.Active && allowedTiers.Contains(promo.MinLoyaltyTier));

        var promotions = await service.GetEligiblePromotionsAsync("Gold");

        promotions.Should().HaveCount(expectedCount);
        promotions.Should().OnlyContain(promo =>
            allowedTiers.Contains(promo.MinLoyaltyTier, StringComparer.OrdinalIgnoreCase));
        promotions.Should().Contain(promo => string.Equals(promo.MinLoyaltyTier, "Silver", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetEligiblePromotionsAsync_InvalidTier_ReturnsEmpty()
    {
        var service = CreateService();

        var promotions = await service.GetEligiblePromotionsAsync("Diamond");

        promotions.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTicketsByCustomerIdAsync_AllTickets_ReturnsAll()
    {
        var service = CreateService();

        var tickets = await service.GetTicketsByCustomerIdAsync("110");

        tickets.Should().HaveCount(1);
        tickets.Should().OnlyContain(ticket => ticket.CustomerId == "110");
    }

    [Fact]
    public async Task GetTicketsByCustomerIdAsync_OpenOnly_ReturnsOpenOnly()
    {
        var service = CreateService();

        var tickets = await service.GetTicketsByCustomerIdAsync("110", openOnly: true);

        tickets.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateTicketAsync_ValidTicket_AssignsGuidIdAndAdds()
    {
        var service = CreateService();
        var ticket = new SupportTicket
        {
            CustomerId = "101",
            Category = "general",
            Subject = "Question",
            Description = "Need help with a product.",
            Status = "open",
            Priority = "low",
            OpenedAt = "2026-03-10",
            ClosedAt = null,
            OrderId = null
        };

        var created = await service.CreateTicketAsync(ticket);
        var tickets = await service.GetTicketsByCustomerIdAsync(ticket.CustomerId);

        created.Id.Should().MatchRegex("^ST-[0-9a-fA-F]{32}$");
        tickets.Should().Contain(existing => existing.Id == created.Id);
    }

    [Fact]
    public async Task CheckConnectivityAsync_Always_ReturnsTrue()
    {
        var service = CreateService();

        var result = await service.CheckConnectivityAsync();

        result.Should().BeTrue();
    }
}
