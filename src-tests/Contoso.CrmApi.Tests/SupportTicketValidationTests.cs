using System.Net;
using System.Net.Http.Json;
using Contoso.CrmApi.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;

namespace Contoso.CrmApi.Tests;

public class SupportTicketValidationTests : IClassFixture<CrmApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public SupportTicketValidationTests(CrmApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CreateTicket_MissingCustomerId_ReturnsValidationProblem()
    {
        var request = CreateValidRequest();
        request.CustomerId = string.Empty;

        var response = await _client.PostAsJsonAsync("/api/v1/tickets", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        problem!.Errors.Should().ContainKey("customer_id");
    }

    [Fact]
    public async Task CreateTicket_MissingCategory_ReturnsValidationProblem()
    {
        var request = CreateValidRequest();
        request.Category = string.Empty;

        var response = await _client.PostAsJsonAsync("/api/v1/tickets", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        problem!.Errors.Should().ContainKey("category");
    }

    [Fact]
    public async Task CreateTicket_InvalidCategory_ReturnsValidationProblem()
    {
        var request = CreateValidRequest();
        request.Category = "invalid";

        var response = await _client.PostAsJsonAsync("/api/v1/tickets", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        problem!.Errors["category"].Single().Should().Contain("category must be one of");
    }

    [Fact]
    public async Task CreateTicket_MissingPriority_ReturnsValidationProblem()
    {
        var request = CreateValidRequest();
        request.Priority = string.Empty;

        var response = await _client.PostAsJsonAsync("/api/v1/tickets", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        problem!.Errors.Should().ContainKey("priority");
    }

    [Fact]
    public async Task CreateTicket_InvalidPriority_ReturnsValidationProblem()
    {
        var request = CreateValidRequest();
        request.Priority = "urgent";

        var response = await _client.PostAsJsonAsync("/api/v1/tickets", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        problem!.Errors["priority"].Single().Should().Contain("priority must be one of");
    }

    [Fact]
    public async Task CreateTicket_MissingSubject_ReturnsValidationProblem()
    {
        var request = CreateValidRequest();
        request.Subject = string.Empty;

        var response = await _client.PostAsJsonAsync("/api/v1/tickets", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        problem!.Errors.Should().ContainKey("subject");
    }

    [Fact]
    public async Task CreateTicket_MissingDescription_ReturnsValidationProblem()
    {
        var request = CreateValidRequest();
        request.Description = string.Empty;

        var response = await _client.PostAsJsonAsync("/api/v1/tickets", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        problem!.Errors.Should().ContainKey("description");
    }

    [Fact]
    public async Task CreateTicket_ValidRequest_Returns201()
    {
        var request = CreateValidRequest();

        var response = await _client.PostAsJsonAsync("/api/v1/tickets", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location!.OriginalString.Should().Contain("/api/v1/tickets/");
        var created = await response.Content.ReadFromJsonAsync<SupportTicket>();
        created!.Id.Should().StartWith("ST-");
        created.Status.Should().Be("open");
        created.CustomerId.Should().Be(request.CustomerId);
    }

    private static CreateTicketRequest CreateValidRequest() =>
        new()
        {
            CustomerId = "101",
            Category = "shipping",
            Priority = "low",
            Subject = "Order status",
            Description = "Checking on shipment status.",
            OrderId = "1001"
        };
}
