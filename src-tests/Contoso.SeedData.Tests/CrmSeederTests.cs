using FluentAssertions;
using seed_data;

namespace Contoso.SeedData.Tests;

public sealed class CrmSeederTests
{
    [Fact]
    public void ParseCsvLine_SimpleFields_ReturnsAllValues()
    {
        var fields = CrmSeeder.ParseCsvLine("101,Emma,Wilson,emma@example.com");

        fields.Should().Equal("101", "Emma", "Wilson", "emma@example.com");
    }

    [Fact]
    public void ParseCsvLine_QuotedFieldWithComma_PreservesComma()
    {
        var fields = CrmSeeder.ParseCsvLine("101,\"742 Evergreen Trail, Portland OR 97201\",Silver");

        fields.Should().Equal("101", "742 Evergreen Trail, Portland OR 97201", "Silver");
    }

    [Fact]
    public void ParseCsvLine_EmptyTrailingFields_PreservesEmpties()
    {
        var fields = CrmSeeder.ParseCsvLine("1,2,,4");

        fields.Should().Equal("1", "2", "", "4");
    }

    [Fact]
    public void ConvertValue_BoolField_ReturnsBool()
    {
        CrmSeeder.ConvertValue("in_stock", "true").Should().Be(true);
        CrmSeeder.ConvertValue("active", "false").Should().Be(false);
    }

    [Fact]
    public void ConvertValue_IntField_ReturnsInt()
    {
        CrmSeeder.ConvertValue("quantity", "42").Should().Be(42);
        CrmSeeder.ConvertValue("discount_percent", "15").Should().Be(15);
    }

    [Fact]
    public void ConvertValue_DecimalField_ReturnsDouble()
    {
        CrmSeeder.ConvertValue("price", "199.95").Should().Be(199.95);
        CrmSeeder.ConvertValue("rating", "4.7").Should().Be(4.7);
    }

    [Fact]
    public void ConvertValue_StringField_ReturnsString()
    {
        CrmSeeder.ConvertValue("first_name", "Emma").Should().Be("Emma");
        CrmSeeder.ConvertValue("email", "emma@example.com").Should().Be("emma@example.com");
    }

    [Fact]
    public void ConvertValue_NullOrEmpty_ReturnsNull()
    {
        CrmSeeder.ConvertValue("price", null).Should().BeNull();
        CrmSeeder.ConvertValue("price", "").Should().BeNull();
    }

    [Fact]
    public void ConvertValue_InvalidIntField_ReturnsRawString()
    {
        // Value falls through to string when parse fails.
        CrmSeeder.ConvertValue("quantity", "not-a-number").Should().Be("not-a-number");
    }

    [Fact]
    public void ParseCsv_RealCustomersFile_ReturnsAllRows()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "customers.csv");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """
            id,first_name,last_name,email,loyalty_tier
            101,Emma,Wilson,emma@example.com,Silver
            102,James,Chen,james@example.com,Bronze
            """);

        var rows = CrmSeeder.ParseCsv(path);

        rows.Should().HaveCount(2);
        rows[0]["id"].Should().Be("101");
        rows[0]["first_name"].Should().Be("Emma");
        rows[1]["loyalty_tier"].Should().Be("Bronze");
    }

    [Fact]
    public void ParseCsv_EmptyFile_ReturnsEmpty()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "empty.csv");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");

        var rows = CrmSeeder.ParseCsv(path);

        rows.Should().BeEmpty();
    }

    [Fact]
    public void ParseCsv_BlankLines_AreSkipped()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "blanks.csv");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "id,name\n101,Emma\n\n102,James\n");

        var rows = CrmSeeder.ParseCsv(path);

        rows.Should().HaveCount(2);
    }

    [Fact]
    public void ParseEntraMapping_StandardPairs_ReturnsAll()
    {
        var result = CrmSeeder.ParseEntraMapping("101=oid-1;102=oid-2;103=oid-3");

        result.Should().HaveCount(3);
        result["101"].Should().Be("oid-1");
        result["102"].Should().Be("oid-2");
        result["103"].Should().Be("oid-3");
    }

    [Fact]
    public void ParseEntraMapping_TrimsWhitespace()
    {
        var result = CrmSeeder.ParseEntraMapping(" 101 = oid-1 ; 102 = oid-2 ");

        result.Should().HaveCount(2);
        result["101"].Should().Be("oid-1");
        result["102"].Should().Be("oid-2");
    }

    [Fact]
    public void ParseEntraMapping_MissingEquals_SkipsEntry()
    {
        var result = CrmSeeder.ParseEntraMapping("101=oid-1;malformed;102=oid-2");

        result.Should().HaveCount(2);
        result.Should().NotContainKey("malformed");
    }

    [Fact]
    public void ParseEntraMapping_EmptyValue_SkipsEntry()
    {
        var result = CrmSeeder.ParseEntraMapping("101=oid-1;102=;103=oid-3");

        result.Should().HaveCount(2);
        result.Should().NotContainKey("102");
    }

    [Fact]
    public void ParseEntraMapping_EmptyString_ReturnsEmpty()
    {
        CrmSeeder.ParseEntraMapping("").Should().BeEmpty();
        CrmSeeder.ParseEntraMapping("   ").Should().BeEmpty();
    }
}
