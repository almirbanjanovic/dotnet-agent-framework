using System.Net;
using System.Text.Json;
using Contoso.CrmApi.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Contoso.CrmApi.Tests;

public class GlobalExceptionHandlerTests
{
    [Fact]
    public async Task TryHandleAsync_ArgumentException_Returns400()
    {
        var (status, problem) = await HandleAsync(new ArgumentException("bad input"));

        status.Should().Be(StatusCodes.Status400BadRequest);
        problem.Title.Should().Be("Bad Request");
    }

    [Fact]
    public async Task TryHandleAsync_KeyNotFoundException_Returns404()
    {
        var (status, problem) = await HandleAsync(new KeyNotFoundException("missing"));

        status.Should().Be(StatusCodes.Status404NotFound);
        problem.Title.Should().Be("Not Found");
    }

    [Fact]
    public async Task TryHandleAsync_CosmosNotFound_Returns404()
    {
        var exception = new CosmosException("not found", HttpStatusCode.NotFound, 0, "activity", 0);
        var (status, problem) = await HandleAsync(exception);

        status.Should().Be(StatusCodes.Status404NotFound);
        problem.Title.Should().Be("Not Found");
    }

    [Fact]
    public async Task TryHandleAsync_CosmosTooManyRequests_Returns429()
    {
        var exception = new CosmosException("too many", HttpStatusCode.TooManyRequests, 0, "activity", 0);
        var (status, problem) = await HandleAsync(exception);

        status.Should().Be(StatusCodes.Status429TooManyRequests);
        problem.Title.Should().Be("Too Many Requests");
    }

    [Fact]
    public async Task TryHandleAsync_CosmosServiceUnavailable_Returns503()
    {
        var exception = new CosmosException("unavailable", HttpStatusCode.ServiceUnavailable, 0, "activity", 0);
        var (status, problem) = await HandleAsync(exception);

        status.Should().Be(StatusCodes.Status503ServiceUnavailable);
        problem.Title.Should().Be("Service Unavailable");
    }

    [Fact]
    public async Task TryHandleAsync_OperationCanceled_Returns499()
    {
        var (status, problem) = await HandleAsync(new OperationCanceledException("cancel"));

        status.Should().Be(StatusCodes.Status499ClientClosedRequest);
        problem.Title.Should().Be("Client Closed Request");
    }

    [Fact]
    public async Task TryHandleAsync_UnhandledException_Returns500()
    {
        var (status, problem) = await HandleAsync(new InvalidOperationException("boom"));

        status.Should().Be(StatusCodes.Status500InternalServerError);
        problem.Title.Should().Be("Internal Server Error");
    }

    private static async Task<(int StatusCode, ProblemDetails Problem)> HandleAsync(Exception exception)
    {
        var logger = Substitute.For<ILogger<GlobalExceptionHandler>>();
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Development);

        var handler = new GlobalExceptionHandler(logger, environment);
        var context = new DefaultHttpContext();
        context.Request.Path = "/test";
        context.Response.Body = new MemoryStream();

        var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);

        handled.Should().BeTrue();
        context.Response.Body.Position = 0;

        var problem = await JsonSerializer.DeserializeAsync<ProblemDetails>(
            context.Response.Body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        problem.Should().NotBeNull();
        return (context.Response.StatusCode, problem!);
    }
}
