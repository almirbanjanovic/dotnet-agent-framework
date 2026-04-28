using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Contoso.CrmApi.Tests;

public class CrmApiIntegrationTests : IClassFixture<CrmApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public CrmApiIntegrationTests(CrmApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task HealthEndpoint_InMemoryMode_Returns200()
    {
        var response = await _client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetCustomerById_UnknownId_Returns404ProblemDetails()
    {
        var response = await _client.GetAsync("/api/v1/customers/unknown");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Title.Should().Be("Not Found");
        problem.Status.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task GetProducts_NoFilters_Returns200WithArray()
    {
        var response = await _client.GetAsync("/api/v1/products");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        var document = JsonDocument.Parse(json);
        document.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        document.RootElement.GetArrayLength().Should().BeGreaterThan(0);
    }
}
