namespace Contoso.FraudWorkflow.Models;

// Inbound refund alert. Posted by the BFF (forwarded from the customer)
// or, in the lab, simulated by hand from the Aspire dashboard.

internal sealed record RefundAlert(
    string CustomerId,
    string OrderId,
    decimal Amount,
    string Reason,
    // Optional ticket the alert was filed against. When set, the workflow
    // will call back to CRM API on every terminal decision so the
    // customer-facing ticket reflects the outcome (auto-approve, operator
    // approve/reject, timeout). When null — the synthetic-alert button on
    // the Operations page — the workflow runs but no callback is fired.
    string? TicketId = null)
{
    // The workflow-instance id. Generated server-side so the workflow has
    // a stable handle for state, logging, and the operator review queue.
    // Note: there is no idempotency key today — retries from the BFF will
    // produce a fresh AlertId and therefore a fresh workflow run. Adding a
    // caller-supplied idempotency key is a stretch exercise (see lab 3).
    public string AlertId { get; init; } = Guid.NewGuid().ToString("N");
}
