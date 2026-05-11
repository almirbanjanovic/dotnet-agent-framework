using Contoso.FraudWorkflow.Models;
using Contoso.FraudWorkflow.Workflows;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Contoso.FraudWorkflow.Endpoints;

internal static class RefundEndpoint
{
    public static IEndpointRouteBuilder MapRefundEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/refunds", HandleAsync);
        return app;
    }

    private static IResult HandleAsync(
        RefundRequest request,
        FraudWorkflowRunner runner,
        IConfiguration configuration,
        IHostApplicationLifetime lifetime)
    {
        if (string.IsNullOrWhiteSpace(request.CustomerId) ||
            string.IsNullOrWhiteSpace(request.OrderId) ||
            request.Amount <= 0 ||
            string.IsNullOrWhiteSpace(request.Reason))
        {
            return Results.BadRequest(new
            {
                error = "InvalidRefundRequest",
                message = "customerId, orderId, amount (>0), and reason are required."
            });
        }

        // The refund threshold is what gates *whether the workflow even runs*.
        // Below the threshold the BFF/customer-facing surface should issue
        // the refund directly; here we belt-and-brace it.
        var threshold = configuration.GetValue<decimal?>("Refund:Threshold") ?? 200m;
        if (request.Amount < threshold)
        {
            return Results.Ok(new
            {
                status = "below_threshold",
                threshold,
                message = "Refund amount is below the risk-review threshold; process directly."
            });
        }

        var alert = new RefundAlert(
            request.CustomerId.Trim(),
            request.OrderId.Trim(),
            request.Amount,
            request.Reason.Trim());

        var alertId = runner.Start(alert, lifetime.ApplicationStopping);

        // 202 Accepted — the work is happening in the background. Caller
        // can poll /api/v1/operations/{alertId} for the eventual outcome.
        return Results.Accepted(
            uri: $"/api/v1/operations/{alertId}",
            value: new { alertId, status = "in_progress" });
    }

    // Wire DTO. Distinct from the internal RefundAlert (which tracks AlertId).
    internal sealed record RefundRequest(string CustomerId, string OrderId, decimal Amount, string Reason);
}
