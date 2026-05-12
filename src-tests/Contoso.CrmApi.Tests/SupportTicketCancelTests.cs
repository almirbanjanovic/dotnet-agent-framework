using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Contoso.CrmApi.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;

namespace Contoso.CrmApi.Tests;

// Behaviour tests for the new ticket-cancellation flow and the
// fire-and-forget refund-alert side-effect on POST /tickets.
public class SupportTicketCancelTests : IClassFixture<CrmApiWebApplicationFactory>
{
    private readonly CrmApiWebApplicationFactory _factory;

    public SupportTicketCancelTests(CrmApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ---------- PATCH /tickets/{id} ----------

    [Fact]
    public async Task PatchTicket_HappyPath_CancelsOpenTicket()
    {
        // Use a freshly created ticket so we don't mutate seeded fixtures
        // that other tests in the class fixture rely on.
        var client = _factory.CreateClient();
        var created = await CreateOpenTicketAsync(client, customerId: "101");

        var patch = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/tickets/{created!.Id}")
        {
            Content = JsonContent.Create(new { status = "cancelled", customer_id = "101" })
        };
        patch.Headers.Add("X-Customer-Entra-Id", "101");

        var response = await client.SendAsync(patch);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<SupportTicket>();
        updated!.Status.Should().Be("cancelled");
        updated.ClosedAt.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task PatchTicket_DifferentCustomer_ReturnsNotFound()
    {
        var client = _factory.CreateClient();
        var created = await CreateOpenTicketAsync(client, customerId: "101");

        var patch = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/tickets/{created!.Id}")
        {
            Content = JsonContent.Create(new { status = "cancelled", customer_id = "102" })
        };
        patch.Headers.Add("X-Customer-Entra-Id", "102");

        var response = await client.SendAsync(patch);

        // 404 (not 403) so an attacker cannot probe which ticket ids exist.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PatchTicket_AlreadyClosed_ReturnsConflict()
    {
        var client = _factory.CreateClient();

        // ST-005 is the only seeded ticket with status="closed" — perfect
        // for the not-open guard. Customer is 110.
        var patch = new HttpRequestMessage(HttpMethod.Patch, "/api/v1/tickets/ST-005")
        {
            Content = JsonContent.Create(new { status = "cancelled", customer_id = "110" })
        };
        patch.Headers.Add("X-Customer-Entra-Id", "110");

        var response = await client.SendAsync(patch);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task PatchTicket_InvalidStatus_ReturnsValidationProblem()
    {
        var client = _factory.CreateClient();
        var created = await CreateOpenTicketAsync(client, customerId: "101");

        var patch = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/tickets/{created!.Id}")
        {
            Content = JsonContent.Create(new { status = "weaponised", customer_id = "101" })
        };
        patch.Headers.Add("X-Customer-Entra-Id", "101");

        var response = await client.SendAsync(patch);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        problem!.Errors.Should().ContainKey("status");
    }

    [Fact]
    public async Task PatchTicket_Unknown_ReturnsNotFound()
    {
        var client = _factory.CreateClient();

        var patch = new HttpRequestMessage(HttpMethod.Patch, "/api/v1/tickets/ST-DOES-NOT-EXIST")
        {
            Content = JsonContent.Create(new { status = "cancelled", customer_id = "101" })
        };
        patch.Headers.Add("X-Customer-Entra-Id", "101");

        var response = await client.SendAsync(patch);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PatchTicket_NoHeader_BodyCustomerIdIsIgnored_Returns401()
    {
        // Adversarial-review fix A1/B1: the body customer_id is no
        // longer trusted as a fallback. A caller without the
        // X-Customer-Entra-Id header gets 401 instead of being able
        // to mutate someone else's ticket via the body.
        var client = _factory.CreateClient();
        var patch = new HttpRequestMessage(HttpMethod.Patch, "/api/v1/tickets/ST-001")
        {
            Content = JsonContent.Create(new { status = "cancelled", customer_id = "104" })
        };
        // NB: deliberately NOT adding the X-Customer-Entra-Id header.

        var response = await client.SendAsync(patch);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---------- POST /tickets — refund-alert side-effect ----------

    [Fact]
    public async Task CreateTicket_ReturnCategoryWithOrder_TriggersFraudWorkflowAlert()
    {
        // Replace the per-test factory's stub so we can assert on the
        // outbound POST /api/v1/refunds. Each test gets its own factory
        // when we want clean handler state.
        await using var factory = new CrmApiWebApplicationFactory();
        var captured = new System.Collections.Concurrent.ConcurrentQueue<string>();
        factory.FraudWorkflowHandler = new StubHttpMessageHandler(req =>
        {
            // Read the body off the synchronous handler thread — by the
            // time the assertion runs the request has been disposed.
            var body = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty;
            captured.Enqueue($"{req.Method} {req.RequestUri?.PathAndQuery} :: {body}");
            return StubHttpMessageHandler.Accepted("{\"alertId\":\"alert-test\"}");
        });

        var client = factory.CreateClient();

        // Customer 103 / Order 1003 exists in seed data (total_amount 349.99).
        var request = new CreateTicketRequest
        {
            CustomerId = "103",
            OrderId = "1003",
            Category = "return",
            Priority = "medium",
            Subject = "Refund please",
            Description = "Tent rainfly arrived torn."
        };

        var response = await client.PostAsJsonAsync("/api/v1/tickets", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        // The fraud-workflow call is detached (Task.Run-equivalent). Poll
        // briefly for it to land — we cap at ~3s so a real regression
        // doesn't hang the suite.
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline && captured.IsEmpty)
        {
            await Task.Delay(50);
        }

        captured.Should().NotBeEmpty("create_support_ticket(category=return) MUST trigger the refund-risk alert");
        var entry = captured.First();
        entry.Should().Contain("POST /api/v1/refunds");
        entry.Should().Contain("\"customerId\":\"103\"");
        entry.Should().Contain("\"orderId\":\"1003\"");
        entry.Should().Contain("349.99"); // amount comes from order, not the ticket
        entry.Should().Contain("\"ticketId\":", "the workflow needs the ticket id to call back on terminal decisions");
    }

    [Fact]
    public async Task CreateTicket_GeneralCategory_DoesNotTriggerFraudWorkflow()
    {
        await using var factory = new CrmApiWebApplicationFactory();
        var hits = 0;
        factory.FraudWorkflowHandler = new StubHttpMessageHandler(_ =>
        {
            Interlocked.Increment(ref hits);
            return StubHttpMessageHandler.Accepted("{}");
        });

        var client = factory.CreateClient();
        var request = new CreateTicketRequest
        {
            CustomerId = "101",
            OrderId = "1001",
            Category = "general", // NOT a refund
            Priority = "low",
            Subject = "Care advice",
            Description = "How do I waterproof my boots?"
        };

        var response = await client.PostAsJsonAsync("/api/v1/tickets", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        // Give the (would-be) background task a chance to run. If it
        // misfires we want to catch it.
        await Task.Delay(500);
        hits.Should().Be(0, "non-return tickets must NOT pull operations away from their lunch");
    }

    [Fact]
    public async Task CreateTicket_ReturnCategoryWithoutOrder_DoesNotTriggerFraudWorkflow()
    {
        await using var factory = new CrmApiWebApplicationFactory();
        var hits = 0;
        factory.FraudWorkflowHandler = new StubHttpMessageHandler(_ =>
        {
            Interlocked.Increment(ref hits);
            return StubHttpMessageHandler.Accepted("{}");
        });

        var client = factory.CreateClient();
        var request = new CreateTicketRequest
        {
            CustomerId = "101",
            OrderId = null, // explicitly no order
            Category = "return",
            Priority = "low",
            Subject = "General refund question",
            Description = "What's your return policy?"
        };

        var response = await client.PostAsJsonAsync("/api/v1/tickets", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        await Task.Delay(500);
        hits.Should().Be(0, "no order id == no refund alert");
    }

    [Fact]
    public async Task CreateTicket_ReturnAgainstUnknownOrder_Returns404AndDoesNotTrigger()
    {
        // Adversarial-review fix A2: the order-ownership guard rejects
        // the ticket creation entirely. The fraud-workflow trigger is
        // unreachable because we 404 before persistence.
        await using var factory = new CrmApiWebApplicationFactory();
        var hits = 0;
        factory.FraudWorkflowHandler = new StubHttpMessageHandler(_ =>
        {
            Interlocked.Increment(ref hits);
            return StubHttpMessageHandler.Accepted("{}");
        });

        var client = factory.CreateClient();
        var request = new CreateTicketRequest
        {
            CustomerId = "101",
            OrderId = "9999999", // not in seed data
            Category = "return",
            Priority = "low",
            Subject = "Refund this order",
            Description = "I'd like a refund."
        };

        var response = await client.PostAsJsonAsync("/api/v1/tickets", request);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await Task.Delay(500);
        hits.Should().Be(0, "no order means no ticket means no alert");
    }

    [Fact]
    public async Task CreateTicket_ReturnAgainstSomeoneElsesOrder_Returns404AndDoesNotTrigger()
    {
        // Adversarial-review fix A2: a customer cannot file a return
        // against another customer's order. The CRM API verifies that
        // order.customer_id matches the resolved customer before
        // persisting the ticket OR firing the alert.
        await using var factory = new CrmApiWebApplicationFactory();
        var hits = 0;
        factory.FraudWorkflowHandler = new StubHttpMessageHandler(_ =>
        {
            Interlocked.Increment(ref hits);
            return StubHttpMessageHandler.Accepted("{}");
        });

        var client = factory.CreateClient();
        var request = new CreateTicketRequest
        {
            CustomerId = "101",       // attacker
            OrderId = "1003",         // belongs to customer 103 in seed data
            Category = "return",
            Priority = "high",
            Subject = "Refund please",
            Description = "I'd like to refund this order."
        };

        var response = await client.PostAsJsonAsync("/api/v1/tickets", request);

        // Same response as "order doesn't exist" — we don't reveal
        // whether an order id is real-but-owned-by-someone-else.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await Task.Delay(500);
        hits.Should().Be(0, "cross-customer order ids must never trigger an alert");
    }

    [Fact]
    public async Task CreateTicket_ReturnWithFraudWorkflowDown_StillReturnsCreated()
    {
        // The fire-and-forget client must swallow upstream failures. If
        // it didn't, an outage of fraud-workflow would block customer
        // ticket creation — which is the exact opposite of what the
        // architecture is supposed to deliver.
        await using var factory = new CrmApiWebApplicationFactory();
        factory.FraudWorkflowHandler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var client = factory.CreateClient();
        var request = new CreateTicketRequest
        {
            CustomerId = "103",
            OrderId = "1003",
            Category = "return",
            Priority = "medium",
            Subject = "Refund please",
            Description = "Defective."
        };

        var response = await client.PostAsJsonAsync("/api/v1/tickets", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // ---------- below-threshold short-circuit ----------

    [Fact]
    public async Task CreateTicket_BelowThresholdResponse_AutoResolvesTicket()
    {
        // When fraud-workflow returns 200 OK with status="below_threshold"
        // (i.e. the order amount is under Refund:Threshold), CRM API must
        // close the loop directly — the workflow won't call back, and
        // leaving the ticket open forever is the bug we are fixing.
        await using var factory = new CrmApiWebApplicationFactory();
        factory.FraudWorkflowHandler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"status\":\"below_threshold\",\"threshold\":200.0}",
                    System.Text.Encoding.UTF8, "application/json")
            });

        var client = factory.CreateClient();

        // Customer 103 / Order 1003 — total 349.99 in seed data, but the
        // stub forces a "below_threshold" reply regardless of amount so
        // we test the response-path branch rather than the threshold math.
        var createReq = new CreateTicketRequest
        {
            CustomerId = "103",
            OrderId = "1003",
            Category = "return",
            Priority = "medium",
            Subject = "Cheap refund",
            Description = "I'd like to return this."
        };
        var createResp = await client.PostAsJsonAsync("/api/v1/tickets", createReq);
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResp.Content.ReadFromJsonAsync<SupportTicket>();

        // Background trigger has to land — poll briefly.
        SupportTicket? refreshed = null;
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            var listResp = await client.GetAsync("/api/v1/customers/103/tickets");
            var tickets = await listResp.Content.ReadFromJsonAsync<List<SupportTicket>>();
            refreshed = tickets!.FirstOrDefault(t => t.Id == created!.Id);
            if (refreshed?.Status == "resolved") break;
            await Task.Delay(50);
        }

        refreshed.Should().NotBeNull("the ticket should still exist");
        refreshed!.Status.Should().Be("resolved",
            "below-threshold returns must close the loop on the customer's ticket");
        refreshed.Comments.Should().NotBeNullOrWhiteSpace(
            "an audit comment should be appended explaining the auto-resolution");
        refreshed.Comments!.Should().Contain("below_threshold");
    }

    [Fact]
    public async Task CreateTicket_AcceptedResponse_DoesNotResolveTicket()
    {
        // When fraud-workflow returns 202 Accepted, the workflow is
        // running in the background and will call back later. CRM API
        // must NOT pre-resolve the ticket — the customer should see
        // "open" until the workflow's callback lands.
        await using var factory = new CrmApiWebApplicationFactory();
        factory.FraudWorkflowHandler = new StubHttpMessageHandler(_ =>
            StubHttpMessageHandler.Accepted("{\"alertId\":\"abc\",\"status\":\"in_progress\"}"));

        var client = factory.CreateClient();
        var createReq = new CreateTicketRequest
        {
            CustomerId = "103",
            OrderId = "1003",
            Category = "return",
            Priority = "medium",
            Subject = "Expensive refund",
            Description = "Big-ticket return."
        };
        var createResp = await client.PostAsJsonAsync("/api/v1/tickets", createReq);
        var created = await createResp.Content.ReadFromJsonAsync<SupportTicket>();

        // Give the background trigger time to run, then verify the
        // ticket is still "open".
        await Task.Delay(750);
        var listResp = await client.GetAsync("/api/v1/customers/103/tickets");
        var tickets = await listResp.Content.ReadFromJsonAsync<List<SupportTicket>>();
        var refreshed = tickets!.First(t => t.Id == created!.Id);

        refreshed.Status.Should().Be("open",
            "above-threshold tickets stay open until fraud-workflow calls back");
    }

    // ---------- POST /internal/tickets/{id}/refund-decision ----------

    [Fact]
    public async Task RefundDecision_Approve_ResolvesTicketAndAppendsComment()
    {
        var client = _factory.CreateClient();
        var ticket = await CreateOpenTicketAsync(client, customerId: "101");

        var body = new
        {
            decision = "approve",
            source = "operator",
            reason = "Verified shipping damage; refund issued.",
            alert_id = "alert-123"
        };

        var response = await client.PostAsJsonAsync(
            $"/api/v1/internal/tickets/{ticket!.Id}/refund-decision", body);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<SupportTicket>();
        updated!.Status.Should().Be("resolved");
        updated.ClosedAt.Should().NotBeNullOrWhiteSpace();
        updated.Comments.Should().Contain("Verified shipping damage");
        updated.Comments!.Should().Contain("operator/approve");
        updated.Comments.Should().Contain("alert-123");
    }

    [Fact]
    public async Task RefundDecision_Reject_LeavesTicketOpenAndAppendsComment()
    {
        var client = _factory.CreateClient();
        var ticket = await CreateOpenTicketAsync(client, customerId: "101");

        var body = new
        {
            decision = "reject",
            source = "operator",
            reason = "Need photos of the damage before we can issue a refund."
        };

        var response = await client.PostAsJsonAsync(
            $"/api/v1/internal/tickets/{ticket!.Id}/refund-decision", body);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<SupportTicket>();
        updated!.Status.Should().Be("open",
            "rejected refunds stay open so the customer can follow up");
        updated.ClosedAt.Should().BeNull();
        updated.Comments.Should().Contain("operator/reject");
        updated.Comments!.Should().Contain("photos of the damage");
    }

    [Fact]
    public async Task RefundDecision_Timeout_LeavesTicketOpenAndAppendsComment()
    {
        var client = _factory.CreateClient();
        var ticket = await CreateOpenTicketAsync(client, customerId: "101");

        var body = new
        {
            decision = "timeout",
            source = "timeout",
            reason = "No operator decision within SLA."
        };

        var response = await client.PostAsJsonAsync(
            $"/api/v1/internal/tickets/{ticket!.Id}/refund-decision", body);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<SupportTicket>();
        updated!.Status.Should().Be("open");
        updated.Comments.Should().Contain("timeout/timeout");
    }

    [Fact]
    public async Task RefundDecision_UnknownTicket_Returns404()
    {
        var client = _factory.CreateClient();

        var body = new { decision = "approve", source = "operator", reason = "n/a" };
        var response = await client.PostAsJsonAsync(
            "/api/v1/internal/tickets/ST-DOES-NOT-EXIST/refund-decision", body);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RefundDecision_InvalidDecision_Returns400()
    {
        var client = _factory.CreateClient();
        var ticket = await CreateOpenTicketAsync(client, customerId: "101");

        var body = new { decision = "ship_a_pony", source = "operator", reason = "n/a" };
        var response = await client.PostAsJsonAsync(
            $"/api/v1/internal/tickets/{ticket!.Id}/refund-decision", body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RefundDecision_RequiresNoCustomerHeader()
    {
        // The callback is service-to-service. It MUST work without an
        // X-Customer-Entra-Id header so fraud-workflow doesn't have to
        // forge a customer identity.
        var client = _factory.CreateClient();
        var ticket = await CreateOpenTicketAsync(client, customerId: "101");

        var body = new { decision = "approve", source = "auto", reason = "Low risk." };
        // Deliberately not adding any X-Customer-Entra-Id header.
        var response = await client.PostAsJsonAsync(
            $"/api/v1/internal/tickets/{ticket!.Id}/refund-decision", body);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RefundDecision_OnAlreadyResolvedTicket_AppendsCommentDoesNotChangeStatus()
    {
        var client = _factory.CreateClient();
        var ticket = await CreateOpenTicketAsync(client, customerId: "101");

        // First decision: approve → resolved.
        var first = await client.PostAsJsonAsync(
            $"/api/v1/internal/tickets/{ticket!.Id}/refund-decision",
            new { decision = "approve", source = "auto", reason = "Auto-approved." });
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        // Second decision lands late (e.g. timeout fired after operator
        // already decided). Status must stay "resolved" but comment is
        // still appended for audit.
        var second = await client.PostAsJsonAsync(
            $"/api/v1/internal/tickets/{ticket!.Id}/refund-decision",
            new { decision = "timeout", source = "timeout", reason = "Late timeout." });
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await second.Content.ReadFromJsonAsync<SupportTicket>();
        updated!.Status.Should().Be("resolved",
            "a late callback must NOT reopen a ticket that was already resolved");
        updated.Comments.Should().Contain("auto/approve");
        updated.Comments!.Should().Contain("timeout/timeout");
    }

    [Fact]
    public async Task RefundDecision_ReasonWithControlChars_IsSanitized()
    {
        // A malicious or buggy reason field cannot inject extra audit
        // lines via newlines or carriage returns.
        var client = _factory.CreateClient();
        var ticket = await CreateOpenTicketAsync(client, customerId: "101");

        var nasty = "Real reason\n[2099-01-01T00:00:00Z forged/approve] Injected line";
        var body = new { decision = "reject", source = "operator", reason = nasty };

        var response = await client.PostAsJsonAsync(
            $"/api/v1/internal/tickets/{ticket!.Id}/refund-decision", body);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await response.Content.ReadFromJsonAsync<SupportTicket>();
        // After sanitization the comment should contain exactly ONE line
        // (no embedded newline) and the forged prefix should not appear
        // at the start of any line.
        updated!.Comments!.Split('\n').Should().HaveCount(1);
        updated.Comments.Should().NotContain("\n[2099");
    }

    [Fact]
    public async Task RefundDecision_ReasonWithFakeBrackets_IsDefanged()
    {
        // Even WITHOUT a newline, a reason like
        //   "[2099-01-01T00:00:00Z forged/approve] hi"
        // could visually masquerade as a real audit line on a UI that
        // splits comments by `\n`. Sanitization replaces square brackets
        // with parentheses to defang the format. (Adversarial review B1.)
        var client = _factory.CreateClient();
        var ticket = await CreateOpenTicketAsync(client, customerId: "101");

        var nasty = "[2099-01-01T00:00:00Z forged/approve] hi";
        var body = new { decision = "reject", source = "operator", reason = nasty };

        var response = await client.PostAsJsonAsync(
            $"/api/v1/internal/tickets/{ticket!.Id}/refund-decision", body);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await response.Content.ReadFromJsonAsync<SupportTicket>();
        updated!.Comments.Should().NotBeNull();
        // The forged bracketed payload must not survive verbatim.
        updated.Comments!.Should().NotContain("[2099");
        // It should appear in the defanged form.
        updated.Comments.Should().Contain("(2099-01-01T00:00:00Z forged/approve)");
    }

    [Fact]
    public async Task RefundDecision_UnknownSource_IsNormalizedToSystem()
    {
        // The audit-line format is `[ts source/decision] reason` — a
        // forged caller submitting source="evil-actor" must NOT see that
        // text appear in the audit prefix. Allowlist it down to "system".
        // (Adversarial review A6.)
        var client = _factory.CreateClient();
        var ticket = await CreateOpenTicketAsync(client, customerId: "101");

        var body = new { decision = "approve", source = "evil-actor", reason = "Spoofed callback." };

        var response = await client.PostAsJsonAsync(
            $"/api/v1/internal/tickets/{ticket!.Id}/refund-decision", body);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await response.Content.ReadFromJsonAsync<SupportTicket>();
        updated!.Comments.Should().NotBeNull();
        updated.Comments!.Should().NotContain("evil-actor");
        // The decision should still apply (unknown source ≠ invalid request) but the
        // visible source MUST be the normalized fallback.
        updated.Comments.Should().Contain("system/approve");
        updated.Status.Should().Be("resolved");
    }

    // ---------- helpers ----------

    private static async Task<SupportTicket?> CreateOpenTicketAsync(HttpClient client, string customerId)
    {
        // Use 'general' so this helper doesn't accidentally trigger the
        // fraud-workflow side-effect we test elsewhere.
        var request = new CreateTicketRequest
        {
            CustomerId = customerId,
            OrderId = null,
            Category = "general",
            Priority = "low",
            Subject = "Test ticket",
            Description = "Created by SupportTicketCancelTests."
        };

        var response = await client.PostAsJsonAsync("/api/v1/tickets", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return await response.Content.ReadFromJsonAsync<SupportTicket>();
    }
}
