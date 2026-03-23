using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;

namespace Contoso.CrmApi.Middleware;

public sealed class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    IHostEnvironment environment) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (statusCode, title) = MapException(exception);

        logger.LogError(
            exception,
            "Unhandled exception — {ExceptionType}: {Message}",
            exception.GetType().Name,
            exception.Message);

        var problemDetails = new ProblemDetails
        {
            Type = $"https://httpstatuses.io/{statusCode}",
            Title = title,
            Status = statusCode,
            Detail = environment.IsDevelopment() ? exception.Message : "An unexpected error occurred.",
            Instance = httpContext.Request.Path
        };

        var traceId = Activity.Current?.Id ?? httpContext.TraceIdentifier;
        problemDetails.Extensions["traceId"] = traceId;

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

        return true;
    }

    private static (int StatusCode, string Title) MapException(Exception exception) => exception switch
    {
        ArgumentException => (StatusCodes.Status400BadRequest, "Bad Request"),
        KeyNotFoundException => (StatusCodes.Status404NotFound, "Not Found"),
        CosmosException cosmos when cosmos.StatusCode == System.Net.HttpStatusCode.NotFound
            => (StatusCodes.Status404NotFound, "Not Found"),
        CosmosException cosmos when cosmos.StatusCode == System.Net.HttpStatusCode.TooManyRequests
            => (StatusCodes.Status429TooManyRequests, "Too Many Requests"),
        CosmosException cosmos when cosmos.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable
            => (StatusCodes.Status503ServiceUnavailable, "Service Unavailable"),
        OperationCanceledException => (StatusCodes.Status499ClientClosedRequest, "Client Closed Request"),
        _ => (StatusCodes.Status500InternalServerError, "Internal Server Error")
    };
}
