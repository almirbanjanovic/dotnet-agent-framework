using Contoso.CrmApi.Models;
using Contoso.CrmApi.Services;
using FluentAssertions;

namespace Contoso.CrmApi.Tests;

public class CsvDataLoaderTests
{
    [Fact]
    public void LoadCustomers_ValidFile_ReturnsAllRows()
    {
        var customers = CsvDataLoader.LoadCustomers(TestDataHelper.GetCsvPath("customers.csv"));

        customers.Should().HaveCount(TestDataHelper.CountCsvRows("customers.csv"));
    }

    [Fact]
    public void LoadCustomers_QuotedFieldWithComma_ParsesCorrectly()
    {
        var customers = CsvDataLoader.LoadCustomers(TestDataHelper.GetCsvPath("customers.csv"));

        var customer = customers.Single(c => c.Id == "101");

        customer.Address.Should().Be("742 Evergreen Trail, Portland OR 97201");
    }

    [Fact]
    public void LoadCustomers_EmptyOptionalField_MapsNull()
    {
        var path = TestDataHelper.GetScratchFilePath("customers-empty-field.csv");
        File.WriteAllText(path,
            "id,first_name,last_name,email,phone,address,loyalty_tier,account_status,created_date" + Environment.NewLine
            + "201,Test,User,,555-9999,\"123 Test St, Test City\",Bronze,active,2025-01-01");

        try
        {
            var customers = CsvDataLoader.LoadCustomers(path);

            customers.Should().ContainSingle();
            customers[0].Email.Should().BeNullOrEmpty();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadProducts_ValidFile_ColumnMappingCorrect()
    {
        var products = CsvDataLoader.LoadProducts(TestDataHelper.GetCsvPath("products.csv"));

        var product = products.Single(p => p.Id == "P001");

        product.Name.Should().Be("TrailBlazer Hiking Boots");
        product.Category.Should().Be("footwear");
        product.Price.Should().Be(189.99m);
        product.InStock.Should().BeTrue();
        product.Rating.Should().BeApproximately(4.6, 0.01);
        product.WeightKg.Should().BeApproximately(0.92, 0.01);
        product.ImageFilename.Should().Be("trailblazer-hiking-boots.png");
    }

    [Fact]
    public void LoadPromotions_ValidFile_BoolAndIntParsing()
    {
        var promotions = CsvDataLoader.LoadPromotions(TestDataHelper.GetCsvPath("promotions.csv"));

        var promo = promotions.Single(p => p.Id == "PROMO-02");

        promo.DiscountPercent.Should().Be(10);
        promo.Active.Should().BeTrue();
    }

    [Fact]
    public void LoadSupportTickets_ValidFile_OptionalClosedAtNull()
    {
        var tickets = CsvDataLoader.LoadSupportTickets(TestDataHelper.GetCsvPath("support-tickets.csv"));

        var ticket = tickets.Single(t => t.Id == "ST-001");

        ticket.ClosedAt.Should().BeNull();
    }

    [Fact]
    public void LoadCsv_EmptyFile_ReturnsEmptyList()
    {
        var path = TestDataHelper.GetScratchFilePath("customers-empty.csv");
        File.WriteAllText(path,
            "id,first_name,last_name,email,phone,address,loyalty_tier,account_status,created_date");

        try
        {
            var customers = CsvDataLoader.LoadCustomers(path);

            customers.Should().BeEmpty();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadCsv_MissingFile_ThrowsFileNotFoundException()
    {
        var path = TestDataHelper.GetScratchFilePath("missing.csv");
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        Action act = () => CsvDataLoader.LoadCustomers(path);

        act.Should().Throw<FileNotFoundException>();
    }
}
