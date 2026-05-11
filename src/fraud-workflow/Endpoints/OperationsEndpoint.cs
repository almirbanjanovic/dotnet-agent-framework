using Contoso.FraudWorkflow.Models;
using Contoso.FraudWorkflow.Services;
using Contoso.FraudWorkflow.Workflows;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Contoso.FraudWorkflow.Endpoints;

internal static class OperationsEndpoint
{
    public static IEndpointRouteBuilder MapOperationsEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/operations/pending", ListPending);
        app.MapPost("/api/v1/operations/decisions", SubmitDecision);
        app.MapGet("/api/v1/operations/{alertId}", GetOutcome);
        return app;
    }

    private static IResult ListPending(IApprovalGate gate)
    {
        var snapshot = gate.ListPending();
        return Results.Ok(snapshot);
    }

    private static IResult SubmitDecision(DecisionRequest request, IApprovalGate gate)
    {
        if (string.IsNullOrWhiteSpace(request.AlertId))
        {
            return Results.BadRequest(new { error = "alertId is required." });
        }

        return gate.SubmitDecision(request.AlertId, request.Decision)
            ? Results.NoContent()
            : Results.NotFound(new
            {
                error = "PendingReviewNotFound",
                message = $"No review currently pending for alert '{request.AlertId}'. " +
                          "It may have already been decided, escalated by timeout, " +
                          "or lost to a process restart on the Local Track."
            });
    }

    private static IResult GetOutcome(string alertId, FraudWorkflowRunner runner) =>
        runner.TryGetOutcome(alertId, out var outcome)
            ? Results.Ok(outcome)
            : Results.NotFound(new
            {
                error = "OutcomeNotFound",
                message = $"No final outcome recorded for alert '{alertId}'. " +
                          "Either the workflow is still running, or it never started."
            });
}
