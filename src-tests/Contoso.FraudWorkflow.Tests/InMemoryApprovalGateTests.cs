using Contoso.FraudWorkflow.Models;
using Contoso.FraudWorkflow.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace Contoso.FraudWorkflow.Tests;

// Locks the human-in-the-loop semantics:
//   1. Submitting a decision unblocks the awaiting task with that decision.
//   2. Listing pending shows the assessment until decided / timed out.
//   3. SubmitDecision returns false for an unknown alertId.
//   4. Adding the same alertId twice is rejected (defense-in-depth).
//   5. Cancellation propagates without leaking entries.

public class InMemoryApprovalGateTests
{
    private static IConfiguration ConfigWithTimeout(string? value) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Refund:ApprovalTimeout"] = value
            })
            .Build();

    private static RefundRiskAssessment Sample(string alertId) =>
        new(alertId, "C001", "O-1", 425m, "demo", 0.5, "manual_review",
            new AgentFinding("h", 0.5, "ok", []),
            new AgentFinding("c", 0.5, "ok", []),
            new AgentFinding("l", 0.5, "ok", []));

    [Fact]
    public async Task SubmitDecision_ReleasesWaiter_WithProvidedDecision()
    {
        var gate = new InMemoryApprovalGate(ConfigWithTimeout("00:01:00"));
        var assessment = Sample("A1");

        var pendingTask = gate.WaitForDecisionAsync(assessment, CancellationToken.None);

        // Allow the gate to register the entry before SubmitDecision races.
        await Task.Yield();
        gate.ListPending().Should().ContainSingle().Which.AlertId.Should().Be("A1");

        gate.SubmitDecision("A1", ApprovalDecision.Approve).Should().BeTrue();

        var decision = await pendingTask;
        decision.Should().Be(ApprovalDecision.Approve);
        gate.ListPending().Should().BeEmpty();
    }

    [Fact]
    public void SubmitDecision_ForUnknownAlert_ReturnsFalse()
    {
        var gate = new InMemoryApprovalGate(ConfigWithTimeout("00:01:00"));

        gate.SubmitDecision("does-not-exist", ApprovalDecision.Approve).Should().BeFalse();
    }

    [Fact]
    public async Task SecondWaitForDecision_WithSameAlert_Throws()
    {
        var gate = new InMemoryApprovalGate(ConfigWithTimeout("01:00:00"));
        var first = gate.WaitForDecisionAsync(Sample("A2"), CancellationToken.None);

        Func<Task> act = () => gate.WaitForDecisionAsync(Sample("A2"), CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();

        // Tear down — release the first to avoid hanging the test.
        gate.SubmitDecision("A2", ApprovalDecision.Reject);
        _ = await first;
    }

    [Fact]
    public async Task Cancellation_RemovesEntry_AndPropagatesAsCancellation()
    {
        // Outer cancellation (e.g. host shutdown) must surface as
        // OperationCanceledException, not as a phantom TimedOut decision.
        // The HumanGateExecutor relies on this distinction so that the
        // workflow runner's `catch (OperationCanceledException)` handler
        // can log shutdown cleanly instead of recording every pending
        // review as a fake operator timeout.
        var gate = new InMemoryApprovalGate(ConfigWithTimeout("01:00:00"));
        using var cts = new CancellationTokenSource();

        var pending = gate.WaitForDecisionAsync(Sample("A3"), cts.Token);
        await Task.Yield();

        cts.Cancel();
        Func<Task> act = () => pending;
        await act.Should().ThrowAsync<OperationCanceledException>();

        gate.ListPending().Should().BeEmpty();
    }

    [Fact]
    public async Task Timeout_FiresWhenNoDecisionArrives()
    {
        var gate = new InMemoryApprovalGate(ConfigWithTimeout("00:00:00.100"));
        var decision = await gate.WaitForDecisionAsync(Sample("A4"), CancellationToken.None);
        decision.Should().Be(ApprovalDecision.TimedOut);
        gate.ListPending().Should().BeEmpty();
    }
}
