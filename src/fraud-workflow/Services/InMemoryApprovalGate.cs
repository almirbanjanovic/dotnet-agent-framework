using System.Collections.Concurrent;
using Contoso.FraudWorkflow.Models;
using Microsoft.Extensions.Configuration;

namespace Contoso.FraudWorkflow.Services;

// In-memory approval gate for the Local Track. State is wiped on
// process restart — that's the deliberate gap that motivates the
// Full Azure Track's Durable-Task-Scheduler-backed implementation.

internal sealed class InMemoryApprovalGate : IApprovalGate
{
    private sealed record PendingEntry(
        RefundRiskAssessment Assessment,
        TaskCompletionSource<ApprovalDecision> Tcs);

    private readonly ConcurrentDictionary<string, PendingEntry> _pending = new();
    private readonly TimeSpan _timeout;

    public InMemoryApprovalGate(IConfiguration configuration)
    {
        // Parses the `Refund:ApprovalTimeout` config string. Use the full
        // TimeSpan format `d.hh:mm:ss` (NOT `hh:mm:ss` — the parser treats
        // a leading integer > 23 as days, so "72:00:00" silently means 72
        // *days* and overflows CancellationTokenSource's ~49-day cap).
        // Default 3 days = 72 hours, matching a typical operations SLA.
        var raw = configuration["Refund:ApprovalTimeout"];

        TimeSpan parsed;
        if (string.IsNullOrWhiteSpace(raw))
        {
            parsed = TimeSpan.FromHours(72);
        }
        else if (!TimeSpan.TryParse(raw, out parsed))
        {
            // Don't silently fall back to 72h on a typo like "72h" or
            // "three days" — surface the misconfig at startup.
            throw new InvalidOperationException(
                $"Refund:ApprovalTimeout '{raw}' is not a valid TimeSpan. " +
                "Use format 'd.hh:mm:ss' (for example, '3.00:00:00' for 72 hours).");
        }

        // CancellationTokenSource(TimeSpan) caps the delay at ~uint.MaxValue
        // milliseconds (~49.7 days). Reject misconfig loudly at startup
        // instead of throwing the same ArgumentOutOfRangeException on the
        // first refund that hits the human gate.
        if (parsed <= TimeSpan.Zero || parsed.TotalMilliseconds > uint.MaxValue - 1)
        {
            throw new InvalidOperationException(
                $"Refund:ApprovalTimeout '{raw}' is invalid. Provide a positive " +
                "duration up to ~49 days, in TimeSpan format 'd.hh:mm:ss' " +
                "(for example, '3.00:00:00' for 72 hours).");
        }

        _timeout = parsed;
    }

    public IReadOnlyCollection<RefundRiskAssessment> ListPending() =>
        _pending.Values.Select(p => p.Assessment).ToArray();

    public async Task<ApprovalDecision> WaitForDecisionAsync(
        RefundRiskAssessment assessment, CancellationToken cancellationToken)
    {
        // RunContinuationsAsynchronously avoids surprise inline continuations
        // that would otherwise run on whatever thread called SubmitDecision.
        var tcs = new TaskCompletionSource<ApprovalDecision>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var entry = new PendingEntry(assessment, tcs);
        if (!_pending.TryAdd(assessment.AlertId, entry))
        {
            // Same alertId already pending — race between two workflow runs
            // for the same alert. Reject the second one rather than silently
            // overwriting the first.
            throw new InvalidOperationException(
                $"Alert {assessment.AlertId} is already awaiting an approval decision.");
        }

        // Race the operator decision against the configured timeout AND the
        // caller's cancellation token. Whichever wins, we always remove the
        // pending entry exactly once.
        //
        // Distinguish the two race losers:
        //   * `_timeout` elapsed       → resolve TCS with ApprovalDecision.TimedOut
        //   * outer `cancellationToken` → propagate as OperationCanceledException
        //
        // The latter matters for clean host shutdown — without the split, an
        // AppHost Ctrl+C would record every pending review as a phantom
        // "operator timed out" outcome instead of being correctly logged as
        // cancelled.
        using var timeoutCts = new CancellationTokenSource(_timeout);
        try
        {
            await using var timeoutReg = timeoutCts.Token.Register(static state =>
            {
                var capture = (PendingEntry)state!;
                capture.Tcs.TrySetResult(ApprovalDecision.TimedOut);
            }, entry).ConfigureAwait(false);

            await using var cancelReg = cancellationToken.Register(static state =>
            {
                var (capture, ct) = ((PendingEntry, CancellationToken))state!;
                capture.Tcs.TrySetCanceled(ct);
            }, (entry, cancellationToken)).ConfigureAwait(false);

            return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            _pending.TryRemove(assessment.AlertId, out _);
        }
    }

    public bool SubmitDecision(string alertId, ApprovalDecision decision)
    {
        if (!_pending.TryGetValue(alertId, out var entry))
        {
            return false;
        }

        // If we cannot complete the TCS (already resolved by timeout, etc.)
        // we still report success-or-failure based on the TCS state. The
        // entry will be removed by the awaiting WaitForDecisionAsync call.
        return entry.Tcs.TrySetResult(decision);
    }
}
