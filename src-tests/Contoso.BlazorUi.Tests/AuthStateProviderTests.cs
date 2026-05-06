using System.Net;
using System.Text;
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

    [Fact]
    public async Task LoadSignedInCustomerAsync_Success_FiresCustomerChangedExactlyOnce()
    {
        // Regression: previously the success path AND the finally block both
        // invoked CustomerChanged, causing subscribers (e.g. Orders.razor)
        // to launch two concurrent loads on first hard refresh and render
        // the same order twice when both responses raced.
        var configuration = new ConfigurationBuilder().Build();
        var environment = new TestWebAssemblyHostEnvironment { EnvironmentName = "Production" };
        var provider = new AuthStateProvider(configuration, environment);

        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.OkJson(
            """
            {"customerId":"107","displayName":"Anna Roberts","email":"anna@example.com"}
            """));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var bffClient = new Contoso.BlazorUi.Services.BffApiClient(httpClient, provider);

        var callCount = 0;
        provider.CustomerChanged += () => callCount++;

        await provider.LoadSignedInCustomerAsync(bffClient);

        callCount.Should().Be(1, "each call must fire CustomerChanged exactly once — " +
            "two events on the success path made Orders.razor double-load and dedup-lessly merge results");
        provider.SelectedCustomer!.Id.Should().Be("107");
        provider.HasAttemptedLoad.Should().BeTrue();
    }

    [Fact]
    public async Task LoadSignedInCustomerAsync_Failure_FiresCustomerChangedExactlyOnce()
    {
        var configuration = new ConfigurationBuilder().Build();
        var environment = new TestWebAssemblyHostEnvironment { EnvironmentName = "Production" };
        var provider = new AuthStateProvider(configuration, environment);

        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var bffClient = new Contoso.BlazorUi.Services.BffApiClient(httpClient, provider);

        var callCount = 0;
        provider.CustomerChanged += () => callCount++;

        await provider.LoadSignedInCustomerAsync(bffClient);

        callCount.Should().Be(1, "failure path must also fire exactly once");
        provider.HasAttemptedLoad.Should().BeTrue();
        provider.LastLoadError.Should().NotBeNullOrWhiteSpace();
    }

    private static AuthStateProvider CreateProvider()
    {
        var configuration = new ConfigurationBuilder().Build();
        var environment = new TestWebAssemblyHostEnvironment { EnvironmentName = "Production" };
        return new AuthStateProvider(configuration, environment);
    }
}
