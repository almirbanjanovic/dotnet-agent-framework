using Contoso.BlazorUi.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace Contoso.BlazorUi.Tests;

public class AuthStateProviderTests
{
    [Fact]
    public void SetCustomer_UpdatesState()
    {
        var provider = CreateProvider();
        var customer = new CustomerOption("200", "Test Customer");

        provider.SetCustomer(customer);

        provider.SelectedCustomer.Should().Be(customer);
        provider.CustomerId.Should().Be("200");
    }

    [Fact]
    public void SetCustomer_NullIgnored()
    {
        var provider = CreateProvider();
        var customer = new CustomerOption("200", "Test Customer");
        provider.SetCustomer(customer);

        provider.SetCustomer(null);

        provider.SelectedCustomer.Should().Be(customer);
    }

    [Fact]
    public void SetCustomer_SameId_NoOp()
    {
        var provider = CreateProvider();
        var customer = new CustomerOption("200", "Test Customer");
        provider.SetCustomer(customer);
        var callCount = 0;
        provider.CustomerChanged += () => callCount++;

        provider.SetCustomer(new CustomerOption("200", "Another Name"));

        callCount.Should().Be(0);
        provider.SelectedCustomer.Should().Be(customer);
    }

    [Fact]
    public void SetCustomer_RaisesCustomerChanged()
    {
        var provider = CreateProvider();
        var callCount = 0;
        provider.CustomerChanged += () => callCount++;

        provider.SetCustomer(new CustomerOption("200", "Test Customer"));

        callCount.Should().Be(1);
    }

    private static AuthStateProvider CreateProvider()
    {
        var configuration = new ConfigurationBuilder().Build();
        var environment = new TestWebAssemblyHostEnvironment { EnvironmentName = "Production" };
        return new AuthStateProvider(configuration, environment);
    }
}
