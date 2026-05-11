using System.Text.Json;
using Contoso.BffApi.Services;

namespace Contoso.BffApi.Endpoints;

// Operator-facing surface that fronts the fraud-workflow service.
//
//  POST /api/v1/refunds                    → kick off a new refund-risk run
//  GET  /api/v1/operations/pending         → list paused human-in-the-loop reviews
//  POST /api/v1/operations/decisions       → resolve a paused review
//  GET  /api/v1/operations/{alertId}       → fetch the final outcome (when complete)
//
// The BFF is a thin proxy: it forwards the request body / response body
// verbatim and only adds (a) authorization, (b) operator-role checks, and
// (c) sanitised error envelopes. All policy lives in the workflow.

internal static class OperationsEndpoints
{
    public static (
        RouteHandlerBuilder submitRefund,
        RouteHandlerBuilder listPending,
        RouteHandlerBuilder submitDecision,
        RouteHandlerBuilder getOutcome) MapOperationsEndpoints(this IEndpointRouteBuilder app)
    {
        var submitRefund   = app.MapPost("/refunds", SubmitRefundAsync);
        var listPending    = app.MapGet ("/operations/pending", ListPendingAsync);
        var submitDecision = app.MapPost("/operations/decisions", SubmitDecisionAsync);
        var getOutcome     = app.MapGet ("/operations/{alertId}", GetOutcomeAsync);
        return (submitRefund, listPending, submitDecision, getOutcome);
    }

    private static async Task<IResult> SubmitRefundAsync(
        JsonElement body,
        FraudWorkflowClient fraudClient,
        CancellationToken ct)
    {
        using var response = await fraudClient.SubmitRefundAsync(body, ct);
        return await ProxyJsonAsync(response, ct);
    }

    private static async Task<IResult> ListPendingAsync(
        FraudWorkflowClient fraudClient,
        CancellationToken ct)
    {
        using var response = await fraudClient.ListPendingAsync(ct);
        return await ProxyJsonAsync(response, ct);
    }

    private static async Task<IResult> SubmitDecisionAsync(
        JsonElement body,
        FraudWorkflowClient fraudClient,
        CancellationToken ct)
    {
        using var response = await fraudClient.SubmitDecisionAsync(body, ct);
        return await ProxyJsonAsync(response, ct);
    }

    private static async Task<IResult> GetOutcomeAsync(
        string alertId,
        FraudWorkflowClient fraudClient,
        CancellationToken ct)
    {
        using var response = await fraudClient.GetOutcomeAsync(alertId, ct);
        return await ProxyJsonAsync(response, ct);
    }

    private static async Task<IResult> ProxyJsonAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var status = (int)response.StatusCode;

        // 204 No Content (e.g. successful decision submission) — nothing to read.
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
        {
            return Results.StatusCode(status);
        }

        // Cap the upstream body to a sane size so a misbehaving downstream
        // can't hand us a multi-GB JSON document. Operations payloads are
        // always small (sub-100KB).
        const int MaxBytes = 1 * 1024 * 1024;
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var bounded = new BoundedReadStream(stream, MaxBytes);
        using var doc = await JsonDocument.ParseAsync(bounded, cancellationToken: ct);

        return Results.Json(doc.RootElement.Clone(), statusCode: status);
    }
}
