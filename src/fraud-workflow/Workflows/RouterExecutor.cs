using Contoso.FraudWorkflow.Models;
using Microsoft.Agents.AI.Workflows;

namespace Contoso.FraudWorkflow.Workflows;

// Entry point of the workflow. Receives the inbound RefundAlert, stashes
// it in shared state for the AggregatorExecutor to read later, and
// returns the alert so the framework forwards it along the workflow's
// outbound edges.
//
// We declare the output type as RefundAlert (`Executor<TIn, TOut>`)
// rather than calling `SendMessageAsync` directly: the framework
// validates outbound message types against this declaration at runtime,
// and an `Executor<TInput>` (input-only) can never send messages of any
// type. Returning the value from `HandleAsync` is the canonical fan-out
// pattern — combined with `AddFanOutEdge` in the WorkflowBuilder, the
// framework duplicates the returned message to every attached executor.

internal sealed class RouterExecutor() : Executor<RefundAlert, RefundAlert>(nameof(RouterExecutor))
{
    public override async ValueTask<RefundAlert> HandleAsync(
        RefundAlert message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        // Stash the original alert in shared state so the AggregatorExecutor
        // can rebuild a full RefundRiskAssessment from the three findings
        // (the findings themselves don't carry the alert).
        await context.QueueStateUpdateAsync(
                AggregatorExecutor.AlertKey, message,
                AggregatorExecutor.AlertScope, cancellationToken)
            .ConfigureAwait(false);

        // Returning the alert hands it to the framework, which fan-outs
        // copies to historyEx / condEx / loyaltyEx via the edge declared
        // in RefundRiskWorkflow.Build.
        return message;
    }
}

