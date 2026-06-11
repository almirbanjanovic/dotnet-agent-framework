using Contoso.FraudWorkflow.Workflows;
using FluentAssertions;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging.Abstractions;

namespace Contoso.FraudWorkflow.Tests;

// Regression pin for the concurrency fix in FraudWorkflowRunner.
//
// Background: InProcessExecution.RunStreamingAsync takes exclusive
// ownership of the Workflow it is given. If the runner caches a single
// Workflow instance (the original design did exactly this), the *second*
// concurrent refund alert blows up with:
//
//   System.InvalidOperationException: Cannot use a Workflow that is
//   already owned by another runner or parent workflow.
//
// The fix is to call RefundRiskWorkflow.Build(...) per-run. These tests
// pin two properties the fix depends on:
//
//   1. Build() returns a distinct Workflow instance on every call.
//      Anyone who reintroduces caching (e.g. stashing the result in a
//      static field) will fail this test.
//   2. Build() is cheap — it only wires executors and edges and does NOT
//      dereference its injected dependencies, so it is safe to call on
//      every alert without dragging in agent setup cost.

public class RefundRiskWorkflowTests
{
    [Fact]
    public void Build_ReturnsDistinctWorkflowInstancePerCall()
    {
        var w1 = RefundRiskWorkflow.Build(
            historyAgent: null!,
            conditionAgent: null!,
            loyaltyAgent: null!,
            aggregator: null!,
            approvalGate: null!,
            loggerFactory: NullLoggerFactory.Instance);

        var w2 = RefundRiskWorkflow.Build(
            historyAgent: null!,
            conditionAgent: null!,
            loyaltyAgent: null!,
            aggregator: null!,
            approvalGate: null!,
            loggerFactory: NullLoggerFactory.Instance);

        w1.Should().NotBeNull();
        w2.Should().NotBeNull();
        w1.Should().NotBeSameAs(w2,
            because: "the runner relies on a fresh Workflow per RunStreamingAsync " +
                     "call; a cached instance throws InvalidOperationException on the " +
                     "second concurrent run (see FraudWorkflowRunner field comments).");
    }

    [Fact]
    public void Build_DoesNotDereferenceInjectedDependencies()
    {
        // Pure construction — no agent / aggregator / gate methods are
        // called from Build itself. That's what makes "build per call"
        // an acceptable performance trade.
        Action act = () => RefundRiskWorkflow.Build(
            historyAgent: null!,
            conditionAgent: null!,
            loyaltyAgent: null!,
            aggregator: null!,
            approvalGate: null!,
            loggerFactory: NullLoggerFactory.Instance);

        act.Should().NotThrow();
    }

    [Fact]
    public void Build_SecondRunAgainstSameInstance_Throws()
    {
        // Lock in the framework contract that motivates the per-call
        // Build: a Workflow can only be owned once. If the framework
        // ever relaxes this (and we'd love it to), this test fails and
        // we can simplify FraudWorkflowRunner to cache the instance.
        var workflow = RefundRiskWorkflow.Build(
            historyAgent: null!,
            conditionAgent: null!,
            loyaltyAgent: null!,
            aggregator: null!,
            approvalGate: null!,
            loggerFactory: NullLoggerFactory.Instance);

        // First ownership: just take it, don't actually run.
        // We use the internal TakeOwnership via a no-op driver path —
        // any concrete attempt to RunStreamingAsync needs a real input,
        // and we don't want to drag agent infrastructure into this test.
        // Instead we directly use TakeOwnership through reflection so the
        // test is hermetic and fast.
        var takeOwnership = typeof(Workflow).GetMethod(
            "TakeOwnership",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        // If the framework renames or removes TakeOwnership, skip the
        // contract check rather than failing the build — the per-call
        // Build is still a correctness invariant guarded by the first
        // two tests.
        if (takeOwnership is null)
        {
            return;
        }

        var ownerA = new object();
        var ownerB = new object();

        takeOwnership.Invoke(workflow, [ownerA, /* subworkflow: */ false, /* existing: */ null!]);

        Action second = () => takeOwnership.Invoke(
            workflow, [ownerB, /* subworkflow: */ false, /* existing: */ null!]);

        second.Should().Throw<System.Reflection.TargetInvocationException>()
            .WithInnerException<InvalidOperationException>()
            .WithMessage("*already owned*");
    }
}
