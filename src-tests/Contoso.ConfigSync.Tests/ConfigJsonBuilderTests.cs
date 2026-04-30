using System.Text.Json.Nodes;
using ConfigSync;
using FluentAssertions;

namespace Contoso.ConfigSync.Tests;

public sealed class ConfigJsonBuilderTests
{
    [Fact]
    public void SetNestedValue_SingleSegment_SetsTopLevelProperty()
    {
        var root = new JsonObject();

        ConfigJsonBuilder.SetNestedValue(root, "Endpoint", "https://example.com");

        root["Endpoint"]!.GetValue<string>().Should().Be("https://example.com");
    }

    [Fact]
    public void SetNestedValue_TwoSegments_CreatesNestedObject()
    {
        var root = new JsonObject();

        ConfigJsonBuilder.SetNestedValue(root, "CosmosDb:Endpoint", "https://cosmos");

        root["CosmosDb"].Should().BeOfType<JsonObject>();
        root["CosmosDb"]!["Endpoint"]!.GetValue<string>().Should().Be("https://cosmos");
    }

    [Fact]
    public void SetNestedValue_DeepNesting_CreatesAllLevels()
    {
        var root = new JsonObject();

        ConfigJsonBuilder.SetNestedValue(root, "A:B:C:D", "value");

        root["A"]!["B"]!["C"]!["D"]!.GetValue<string>().Should().Be("value");
    }

    [Fact]
    public void SetNestedValue_MultipleKeys_SharesIntermediateNodes()
    {
        var root = new JsonObject();

        ConfigJsonBuilder.SetNestedValue(root, "CosmosDb:Endpoint", "https://cosmos");
        ConfigJsonBuilder.SetNestedValue(root, "CosmosDb:DatabaseName", "contoso-crm");

        var cosmos = root["CosmosDb"].Should().BeOfType<JsonObject>().Subject;
        cosmos["Endpoint"]!.GetValue<string>().Should().Be("https://cosmos");
        cosmos["DatabaseName"]!.GetValue<string>().Should().Be("contoso-crm");
    }

    [Fact]
    public void SetNestedValue_OverwritesExisting()
    {
        var root = new JsonObject { ["Key"] = "old" };

        ConfigJsonBuilder.SetNestedValue(root, "Key", "new");

        root["Key"]!.GetValue<string>().Should().Be("new");
    }

    [Fact]
    public void SetNestedValue_EmptyValue_StillStored()
    {
        var root = new JsonObject();

        ConfigJsonBuilder.SetNestedValue(root, "AzureAd:TenantId", "");

        root["AzureAd"]!["TenantId"]!.GetValue<string>().Should().Be("");
    }

    [Fact]
    public void SetNestedValue_NullRoot_Throws()
    {
        var act = () => ConfigJsonBuilder.SetNestedValue(null!, "Key", "value");

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SetNestedValue_BlankConfigKey_Throws(string? configKey)
    {
        var root = new JsonObject();

        var act = () => ConfigJsonBuilder.SetNestedValue(root, configKey!, "value");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SetNestedValue_RealWorldManifest_BuildsExpectedShape()
    {
        // Simulates the crm-api manifest entries from config-sync.
        var root = new JsonObject();

        ConfigJsonBuilder.SetNestedValue(root, "CosmosDb:Endpoint", "https://cosmos.example.com");
        ConfigJsonBuilder.SetNestedValue(root, "CosmosDb:DatabaseName", "contoso-crm");
        ConfigJsonBuilder.SetNestedValue(root, "AzureAd:TenantId", "tenant-id");

        var json = root.ToJsonString();
        json.Should().Contain("\"CosmosDb\"");
        json.Should().Contain("\"DatabaseName\":\"contoso-crm\"");
        json.Should().Contain("\"AzureAd\"");
    }
}
