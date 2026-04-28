using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace Contoso.BffApi.Tests;

public class GetConfigOrDefaultTests
{
    [Fact]
    public void GetConfigOrDefault_NullValue_ReturnsDefault()
    {
        var config = new ConfigurationBuilder().Build();

        var result = InvokeGetConfigOrDefault(config, "missing", "default");

        result.Should().Be("default");
    }

    [Fact]
    public void GetConfigOrDefault_EmptyString_ReturnsDefault()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["value"] = string.Empty })
            .Build();

        var result = InvokeGetConfigOrDefault(config, "value", "default");

        result.Should().Be("default");
    }

    [Fact]
    public void GetConfigOrDefault_ValidValue_ReturnsValue()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["value"] = "http://x" })
            .Build();

        var result = InvokeGetConfigOrDefault(config, "value", "default");

        result.Should().Be("http://x");
    }

    private static string InvokeGetConfigOrDefault(IConfiguration config, string key, string defaultValue)
    {
        var method = typeof(Program).GetMethod(
            "GetConfigOrDefault",
            BindingFlags.Static | BindingFlags.NonPublic);

        method.Should().NotBeNull();
        return (string)method!.Invoke(null, new object?[] { config, key, defaultValue })!;
    }
}
