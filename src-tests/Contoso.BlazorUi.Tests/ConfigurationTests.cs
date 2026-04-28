using Contoso.BlazorUi.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace Contoso.BlazorUi.Tests;

public class ConfigurationTests
{
    [Fact]
    public void GetBffBaseUrl_NoConfig_ReturnsDefault()
    {
        var configuration = new ConfigurationBuilder().Build();

        var baseUrl = BlazorUiConfiguration.GetBffBaseUrl(configuration);

        baseUrl.Should().Be(BlazorUiConfiguration.DefaultBffBaseUrl);
    }

    [Fact]
    public void GetBffBaseUrl_CustomConfig_OverridesDefault()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Bff:BaseUrl"] = "https://bff.example.com" })
            .Build();

        var baseUrl = BlazorUiConfiguration.GetBffBaseUrl(configuration);

        baseUrl.Should().Be("https://bff.example.com");
    }

    [Fact]
    public void IsDevAuthEnabled_DataModeFlag_EnablesDevAuth()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DataMode"] = "InMemory" })
            .Build();
        var environment = new TestWebAssemblyHostEnvironment { EnvironmentName = "Production" };

        var result = BlazorUiConfiguration.IsDevAuthEnabled(configuration, environment);

        result.Should().BeTrue();
    }
}
