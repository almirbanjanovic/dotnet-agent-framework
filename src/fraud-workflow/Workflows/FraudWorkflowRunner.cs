using System.Collections.Concurrent;
using Contoso.FraudWorkflow.Agents;
using Contoso.FraudWorkflow.Models;
using Contoso.FraudWorkflow.Services;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;

namespace Contoso.FraudWorkflow.Workflows;

// Owns the long-lived Workflow instance and starts a background Run for
// every inbound RefundAlert. The HTTP /refunds endpoint returns
// immediately with the alertId; the run continues in the background and
// pauses at the human gate (potentially for hours) until an operator
// posts a decision via /operations/decisions.
//
// Local Track only: completed-run outcomes are kept in a bounded LRU so
// the operator UI can still show "what happened" after a decision lands
// without polluting the pending list. A fresh process restart drops both
// (matches the InMemoryApprovalGate's documented restart behaviour).

internal sealed class FraudWorkflowRunner
{
    // Bounded so a long-running process cannot accumulate unbounded state
    // even if operators never look at the outcomes page.
    private const int MaxRetainedOutcomes = 200;

    private readonly Workflow _workflow;
    private readonly ILogger<FraudWorkflowRunner> _logger;
    private readonly ConcurrentDictionary<string, FinalAction> _outcomes = new();
    private readonly ConcurrentQueue<string> _outcomeOrder = new();

    public FraudWorkflowRunner(
        OrderHistoryAgent historyAgent,
        ReturnConditionAgent conditionAgent,
        LoyaltyContextAgent loyaltyAgent,
        RiskAggregator aggregator,
        IApprovalGate approvalGate,
        ILoggerFactory loggerFactory)
    {
        _workflow = RefundRiskWorkflow.Build(
            historyAgent, conditionAgent, loyaltyAgent, aggregator, approvalGate, loggerFactory);
        _logger = loggerFactory.CreateLogger<FraudWorkflowRunner>();
    }

    public string Start(RefundAlert alert, CancellationToken applicationStopping)
    {
        var alertId = alert.AlertId;
        _logger.LogInformation(
            "Starting refund-risk workflow for alert {AlertId} (customer {CustomerId}, order {OrderId}, ${Amount}).",
            alertId, alert.CustomerId, alert.OrderId, alert.Amount);

        // Fire-and-forget background task. The task lives until the workflow
        // either reaches a terminal state OR the host shuts down. We do NOT
        // tie the workflow to any individual HTTP request's cancellation
        // token — if we did, hitting Ctrl+C on the dashboard would kill an
        // operator-pending workflow.
        _ = Task.Run(() => RunAsync(alert, applicationStopping), applicationStopping);

        return alertId;
    }

    public bool TryGetOutcome(string alertId, out FinalAction outcome)
        => _outcomes.TryGetValue(alertId, out outcome!);

    public IReadOnlyCollection<FinalAction> ListOutcomes() => _outcomes.Values.ToArray();

    private async Task RunAsync(RefundAlert alert, CancellationToken ct)
    {
        try
        {
            // RunStreamingAsync + WatchStreamAsync drives the workflow
            // through every superstep and yields events as they arrive.
            // The plain `RunAsync` snapshots events from a single drive
            // and returns immediately — for a multi-stage workflow
            // (router → fan-out → 3 agents → barrier → aggregator → gate)
            // that means we'd see the first superstep's events only and
            // never observe the FinalAction emitted by the gate.
            await using var run = await InProcessExecution
                .RunStreamingAsync(_workflow, alert, cancellationToken: ct)
                .ConfigureAwait(false);

            FinalAction? final = null;
            await foreach (var evt in run.WatchStreamAsync(ct).ConfigureAwait(false))
            {
                switch (evt)
                {
                    case WorkflowOutputEvent output when output.Data is FinalAction action:
                        final = action;
                        break;
                    case ExecutorFailedEvent failed:
                        _logger.LogError(
                            "Executor {ExecutorId} failed during refund-risk workflow for alert {AlertId}: {Data}",
                            failed.ExecutorId, alert.AlertId, failed.Data);
                        break;
                    case WorkflowErrorEvent error:
                        _logger.LogError(
                            error.Exception,
                            "Refund-risk workflow for alert {AlertId} produced an error event.",
                            alert.AlertId);
                        break;
                }
            }

            if (final is not null)
            {
                StoreOutcome(final);
                _logger.LogInformation(
                    "Refund-risk workflow completed for alert {AlertId}: {Decision} (source: {Source}).",
                    alert.AlertId, final.Decision, final.Source);
            }
            else
            {
                _logger.LogWarning(
                    "Refund-risk workflow for alert {AlertId} produced no FinalAction output.",
                    alert.AlertId);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogInformation(
                "Refund-risk workflow for alert {AlertId} cancelled because the host is stopping.",
                alert.AlertId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Refund-risk workflow for alert {AlertId} threw an unhandled exception.",
                alert.AlertId);
        }
    }

    private void StoreOutcome(FinalAction outcome)
    {
        // _outcomes (ConcurrentDictionary) and _outcomeOrder (ConcurrentQueue)
        // are each thread-safe individually, but the combined check-and-prune
        // below is a multi-step compound operation. Without the lock,
        // concurrent workflow completions can race past the cap or evict the
        // wrong entries. Single, narrowly-scoped lock is sufficient because
        // the work inside is bounded and lock-free.
        lock (_outcomeOrder)
        {
            _outcomes[outcome.AlertId] = outcome;
            _outcomeOrder.Enqueue(outcome.AlertId);

            while (_outcomeOrder.Count > MaxRetainedOutcomes &&
                   _outcomeOrder.TryDequeue(out var oldest))
            {
                // Skip removing the just-stored outcome's slot if a
                // duplicate AlertId puts an older copy at the queue head.
                if (string.Equals(oldest, outcome.AlertId, StringComparison.Ordinal))
                {
                    continue;
                }
                _outcomes.TryRemove(oldest, out _);
            }
        }
    }
}
