using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Contoso.FraudWorkflow.Models;
using Contoso.FraudWorkflow.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Contoso.FraudWorkflow.Tests;

// Wire-format tests for the outbound callback to crm-api. The CrmApiClient
// is internal to fraud-workflow but the assembly's InternalsVisibleTo
// attribute (added so the runner can call it) lets the tests project
// reach in.
public class CrmApiClientTests
{
    [Fact]
    public async Task ApplyRefundDecision_PostsToInternalCallbackPath()
    {
        var stub = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var http = new HttpClient(stub) { BaseAddress = new Uri("http://crm-api.test") };
        var client = new CrmApiClient(http, NullLogger<CrmApiClient>.Instance);

        var assessment = new RefundRiskAssessment(
            AlertId: "alert-9",
            CustomerId: "C001",
            OrderId: "O-1234",
            Amount: 425.50m,
            Reason: "Damaged.",
            OverallRiskScore: 0.2,
            RecommendedAction: "approve",
            HistoryFinding: new AgentFinding("h", 0.1, "ok", []),
            ConditionFinding: new AgentFinding("c", 0.2, "ok", []),
            LoyaltyFinding: new AgentFinding("l", 0.0, "ok", []),
            TicketId: "ST-abc123");
        var action = FinalAction.AutoApprove(assessment);

        var ok = await client.ApplyRefundDecisionAsync(action);
        ok.Should().BeTrue();

        var body = stub.LastBody;
        var captured = stub.LastRequest!;
        captured.Method.Should().Be(HttpMethod.Post);
        captured.RequestUri!.AbsolutePath.Should().Be("/api/v1/internal/tickets/ST-abc123/refund-decision");

        body.GetProperty("decision").GetString().Should().Be("approve");
        body.GetProperty("source").GetString().Should().Be("auto");
        body.GetProperty("reason").GetString().Should().Contain("Auto-approved");
        body.GetProperty("alert_id").GetString().Should().Be("alert-9");
    }

    [Fact]
    public async Task ApplyRefundDecision_NoTicketId_ReturnsFalseAndDoesNotPost()
    {
        var stub = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var http = new HttpClient(stub) { BaseAddress = new Uri("http://crm-api.test") };
        var client = new CrmApiClient(http, NullLogger<CrmApiClient>.Instance);

        // Synthetic alert (no originating ticket).
        var assessment = new RefundRiskAssessment(
            AlertId: "alert-9", CustomerId: "C001", OrderId: "O-1", Amount: 1m, Reason: "x",
            OverallRiskScore: 0.1, RecommendedAction: "approve",
            HistoryFinding: new AgentFinding("h", 0.0, "ok", []),
            ConditionFinding: new AgentFinding("c", 0.0, "ok", []),
            LoyaltyFinding: new AgentFinding("l", 0.0, "ok", []),
            TicketId: null);
        var action = FinalAction.AutoApprove(assessment);

        var ok = await client.ApplyRefundDecisionAsync(action);
        ok.Should().BeFalse();
        stub.RequestCount.Should().Be(0);
    }

    [Fact]
    public async Task ApplyRefundDecision_404FromCrmApi_ReturnsFalse()
    {
        // Customer cancelled their ticket mid-review — fraud-workflow
        // arrives with a callback for an id the CRM API no longer knows
        // about. The client must treat this as a benign "nothing to do"
        // and not throw.
        var stub = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var http = new HttpClient(stub) { BaseAddress = new Uri("http://crm-api.test") };
        var client = new CrmApiClient(http, NullLogger<CrmApiClient>.Instance);

        var assessment = new RefundRiskAssessment(
            AlertId: "alert-9", CustomerId: "C001", OrderId: "O-1", Amount: 1m, Reason: "x",
            OverallRiskScore: 0.1, RecommendedAction: "approve",
            HistoryFinding: new AgentFinding("h", 0.0, "ok", []),
            ConditionFinding: new AgentFinding("c", 0.0, "ok", []),
            LoyaltyFinding: new AgentFinding("l", 0.0, "ok", []),
            TicketId: "ST-stale");
        var action = FinalAction.AutoApprove(assessment);

        var ok = await client.ApplyRefundDecisionAsync(action);
        ok.Should().BeFalse();
    }

    [Fact]
    public async Task ApplyRefundDecision_Reject_MapsToRejectDecision()
    {
        var stub = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var http = new HttpClient(stub) { BaseAddress = new Uri("http://crm-api.test") };
        var client = new CrmApiClient(http, NullLogger<CrmApiClient>.Instance);

        var assessment = new RefundRiskAssessment(
            AlertId: "alert-9", CustomerId: "C001", OrderId: "O-1", Amount: 1m, Reason: "x",
            OverallRiskScore: 0.5, RecommendedAction: "manual_review",
            HistoryFinding: new AgentFinding("h", 0.5, "ok", []),
            ConditionFinding: new AgentFinding("c", 0.5, "ok", []),
            LoyaltyFinding: new AgentFinding("l", 0.5, "ok", []),
            TicketId: "ST-x");
        var action = FinalAction.FromOperator(assessment, ApprovalDecision.Reject);

        await client.ApplyRefundDecisionAsync(action);
        stub.LastBody.GetProperty("decision").GetString().Should().Be("reject");
        stub.LastBody.GetProperty("source").GetString().Should().Be("operator");
    }

    [Fact]
    public async Task ApplyRefundDecision_Timeout_MapsToTimeoutDecision()
    {
        var stub = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var http = new HttpClient(stub) { BaseAddress = new Uri("http://crm-api.test") };
        var client = new CrmApiClient(http, NullLogger<CrmApiClient>.Instance);

        var assessment = new RefundRiskAssessment(
            AlertId: "alert-9", CustomerId: "C001", OrderId: "O-1", Amount: 1m, Reason: "x",
            OverallRiskScore: 0.7, RecommendedAction: "escalate",
            HistoryFinding: new AgentFinding("h", 0.7, "ok", []),
            ConditionFinding: new AgentFinding("c", 0.7, "ok", []),
            LoyaltyFinding: new AgentFinding("l", 0.7, "ok", []),
            TicketId: "ST-x");
        var action = FinalAction.Timeout(assessment);

        await client.ApplyRefundDecisionAsync(action);
        stub.LastBody.GetProperty("decision").GetString().Should().Be("timeout");
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public int RequestCount { get; private set; }
        public HttpRequestMessage? LastRequest { get; private set; }
        public JsonElement LastBody { get; private set; }

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            LastRequest = request;
            if (request.Content is not null)
            {
                var raw = await request.Content.ReadAsStringAsync(cancellationToken);
                if (!string.IsNullOrEmpty(raw))
                {
                    using var doc = JsonDocument.Parse(raw);
                    LastBody = doc.RootElement.Clone();
                }
            }
            return _responder(request);
        }
    }
}
