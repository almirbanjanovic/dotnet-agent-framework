using System.Net;
using System.Net.Http.Json;
using Contoso.CrmApi.Models;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Contoso.CrmApi.Tests;

// Behaviour tests for the return / refund order-status mirroring.
//
// Two real-world bugs are covered:
//   1. The customer could file `category=return` on an order that
//      hadn't been delivered yet (still "shipped"/"processing"). The
//      server now rejects with 409 Conflict.
//   2. After a return was approved the order's status didn't change,
//      so the customer's UI kept lying. The server now mirrors the
//      refund outcome onto the order:
//         create return ticket   → order: delivered → return-started
//         approve / below_thr.   → order: return-started → returned
//         customer cancels ticket → order: return-started → delivered
//         reject / timeout       → order untouched (ticket stays open)
//
// Each test that mutates seed state uses its own per-test factory so
// the fresh in-memory seed is unaffected by sibling tests.
public class SupportTicketReturnFlowTests
{
    // ---------- eligibility gate at ticket creation ----------

    [Fact]
    public async Task CreateTicket_Return_OnShippedOrder_Returns409Conflict()
    {
        // Order 1009 belongs to customer 109 and ships in seed data.
        // Customer asks for a return → server must refuse.
        await using var factory = new CrmApiWebApplicationFactory();
        var client = factory.CreateClient();

        var request = new CreateTicketRequest
        {
            CustomerId = "109",
            OrderId = "1009",
            Category = "return",
            Priority = "medium",
            Subject = "Refund please",
            Description = "I want to send this back."
        };

        var response = await client.PostAsJsonAsync("/api/v1/tickets", request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "you cannot return an order that hasn't been delivered yet");

        // The body must contain a customer-readable explanation that
        // surfaces verbatim through the agent.
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("delivered",
            "the message should tell the customer to wait for delivery");
        body.Should().Contain("shipped",
            "the message should report the current status so the agent can explain");

        // Order must not be mutated by a rejected request.
        var orderResp = await client.GetAsync("/api/v1/orders/1009");
        var order = await orderResp.Content.ReadFromJsonAsync<Order>();
        order!.Status.Should().Be("shipped",
            "a rejected ticket must not move the order");
    }

    [Fact]
    public async Task CreateTicket_Return_OnProcessingOrder_Returns409Conflict()
    {
        // Order 1005 belongs to customer 105, status=processing in seed data.
        await using var factory = new CrmApiWebApplicationFactory();
        var client = factory.CreateClient();

        var request = new CreateTicketRequest
        {
            CustomerId = "105",
            OrderId = "1005",
            Category = "return",
            Priority = "low",
            Subject = "Refund",
            Description = "Want to cancel this."
        };

        var response = await client.PostAsJsonAsync("/api/v1/tickets", request);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CreateTicket_Return_OnAlreadyReturnedOrder_Returns409Conflict()
    {
        // Order 1008 belongs to customer 108, status=returned in seed data.
        // A customer cannot re-file a return on an already-returned order.
        await using var factory = new CrmApiWebApplicationFactory();
        var client = factory.CreateClient();

        var request = new CreateTicketRequest
        {
            CustomerId = "108",
            OrderId = "1008",
            Category = "return",
            Priority = "low",
            Subject = "Refund again",
            Description = "Want to return this."
        };

        var response = await client.PostAsJsonAsync("/api/v1/tickets", request);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("already been returned");
    }

    [Fact]
    public async Task CreateTicket_Return_AgainstSeededReturnStartedOrder_Returns409Conflict()
    {
        // Adversarial-review R1 #1: the seed has order 1002 in
        // `return-started` because ST-003 is an open return for it.
        // A second return file against the same order MUST fail.
        await using var factory = new CrmApiWebApplicationFactory();
        var client = factory.CreateClient();

        var request = new CreateTicketRequest
        {
            CustomerId = "102",
            OrderId = "1002",
            Category = "return",
            Priority = "medium",
            Subject = "Second return",
            Description = "Forgot I already filed one."
        };

        var response = await client.PostAsJsonAsync("/api/v1/tickets", request);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("already been started");
    }

    [Fact]
    public async Task RefundDecision_Approve_SelfHealsWhenOrderStillDelivered()
    {
        // Adversarial-review R1 #2: if the create-time order flip
        // failed silently (try/catch logs and continues), the order
        // would still be `delivered` when an approve callback lands.
        // The approve path must self-heal — accept either
        // `return-started` OR `delivered` and land at `returned`.
        //
        // We simulate "create-time flip failed" by directly resetting
        // the order's status back to delivered after the create.
        await using var factory = new CrmApiWebApplicationFactory();
        var client = factory.CreateClient();

        var ticket = await OpenReturnTicketAsync(client, customerId: "101", orderId: "1001");

        // Force the recovery scenario by reaching into the in-memory
        // service and resetting the order. (In a real Cosmos failure
        // scenario the order would have been left at `delivered`
        // because the upsert never landed.)
        using (var scope = factory.Services.CreateScope())
        {
            var cosmos = scope.ServiceProvider
                .GetRequiredService<Contoso.CrmApi.Services.ICosmosService>();
            var order = await cosmos.GetOrderByIdAsync("1001");
            order!.Status = "delivered";
            await cosmos.UpdateOrderAsync(order);
        }

        var body = new { decision = "approve", source = "operator", reason = "ok" };
        var response = await client.PostAsJsonAsync(
            $"/api/v1/internal/tickets/{ticket!.Id}/refund-decision", body);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var orderResp = await client.GetAsync("/api/v1/orders/1001");
        var orderAfter = await orderResp.Content.ReadFromJsonAsync<Order>();
        orderAfter!.Status.Should().Be("returned",
            "approval must always land the order in `returned` even if the create-time flip was lost");
    }

    [Fact]
    public async Task CreateTicket_Return_OnDeliveredOrder_FlipsOrderToReturnStarted()
    {
        // Emma's order 1001 is delivered in seed data. Filing a return
        // must succeed AND immediately reflect on the order.
        await using var factory = new CrmApiWebApplicationFactory();
        var client = factory.CreateClient();

        var request = new CreateTicketRequest
        {
            CustomerId = "101",
            OrderId = "1001",
            Category = "return",
            Priority = "medium",
            Subject = "Refund please",
            Description = "Boots don't fit."
        };

        var response = await client.PostAsJsonAsync("/api/v1/tickets", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        // Order's status must reflect the open return.
        var orderResp = await client.GetAsync("/api/v1/orders/1001");
        var order = await orderResp.Content.ReadFromJsonAsync<Order>();
        order!.Status.Should().Be("return-started",
            "the order should mirror the open return so the customer's UI doesn't lie");
    }

    [Fact]
    public async Task CreateTicket_Return_OnAlreadyReturnStartedOrder_Returns409Conflict()
    {
        // After filing one return, a second one against the same order
        // must be refused — the order is no longer in `delivered`.
        await using var factory = new CrmApiWebApplicationFactory();
        var client = factory.CreateClient();

        var first = new CreateTicketRequest
        {
            CustomerId = "101",
            OrderId = "1001",
            Category = "return",
            Priority = "medium",
            Subject = "First return",
            Description = "Wrong size."
        };
        var firstResp = await client.PostAsJsonAsync("/api/v1/tickets", first);
        firstResp.StatusCode.Should().Be(HttpStatusCode.Created);

        var second = new CreateTicketRequest
        {
            CustomerId = "101",
            OrderId = "1001",
            Category = "return",
            Priority = "medium",
            Subject = "Second return",
            Description = "Changed my mind again."
        };
        var secondResp = await client.PostAsJsonAsync("/api/v1/tickets", second);
        secondResp.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var body = await secondResp.Content.ReadAsStringAsync();
        body.Should().Contain("already been started",
            "the message should point the customer to the existing ticket");
    }

    // ---------- non-return categories must not gate ----------

    [Fact]
    public async Task CreateTicket_ProductIssue_OnShippedOrder_StillSucceeds()
    {
        // Reporting a product-issue (e.g. damaged in transit) does NOT
        // gate on order delivered — only refunds do.
        await using var factory = new CrmApiWebApplicationFactory();
        var client = factory.CreateClient();

        var request = new CreateTicketRequest
        {
            CustomerId = "109",
            OrderId = "1009",
            Category = "product-issue",
            Priority = "low",
            Subject = "Tracking update",
            Description = "Question about shipping."
        };

        var response = await client.PostAsJsonAsync("/api/v1/tickets", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        // Order must not be mutated — only the return flow flips status.
        var orderResp = await client.GetAsync("/api/v1/orders/1009");
        var order = await orderResp.Content.ReadFromJsonAsync<Order>();
        order!.Status.Should().Be("shipped");
    }

    // ---------- refund-decision callback mirrors order ----------

    [Fact]
    public async Task RefundDecision_Approve_FlipsOrderToReturned()
    {
        // Stub fraud-workflow with a 202 so the create path doesn't
        // auto-resolve via the below-threshold short-circuit; we want
        // to test the explicit /internal/.../refund-decision callback.
        await using var factory = new CrmApiWebApplicationFactory();
        var client = factory.CreateClient();

        var ticket = await OpenReturnTicketAsync(client, customerId: "101", orderId: "1001");

        var body = new
        {
            decision = "approve",
            source = "operator",
            reason = "Verified.",
            alert_id = "alert-xyz"
        };
        var response = await client.PostAsJsonAsync(
            $"/api/v1/internal/tickets/{ticket!.Id}/refund-decision", body);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var orderResp = await client.GetAsync("/api/v1/orders/1001");
        var order = await orderResp.Content.ReadFromJsonAsync<Order>();
        order!.Status.Should().Be("returned",
            "an approved refund must mark the order as returned");
    }

    [Fact]
    public async Task RefundDecision_Reject_LeavesOrderInReturnStarted()
    {
        // Per the existing ApplyDecisionToTicket contract, reject leaves
        // the ticket in "open" status. Therefore the order also stays
        // in "return-started" until the customer cancels the ticket.
        await using var factory = new CrmApiWebApplicationFactory();
        var client = factory.CreateClient();

        var ticket = await OpenReturnTicketAsync(client, customerId: "101", orderId: "1001");

        var body = new
        {
            decision = "reject",
            source = "operator",
            reason = "Need photos."
        };
        var response = await client.PostAsJsonAsync(
            $"/api/v1/internal/tickets/{ticket!.Id}/refund-decision", body);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var orderResp = await client.GetAsync("/api/v1/orders/1001");
        var order = await orderResp.Content.ReadFromJsonAsync<Order>();
        order!.Status.Should().Be("return-started",
            "rejected refunds keep the ticket open so the order also stays in the requested state");
    }

    [Fact]
    public async Task RefundDecision_Approve_TwiceIsIdempotentOnOrder()
    {
        // The require-from guard on the order-flip helper means the
        // second approve callback (after the ticket is already
        // `resolved`) does NOT trample the order. The ticket itself
        // also stays resolved per the existing ApplyDecisionToTicket
        // idempotency rule.
        await using var factory = new CrmApiWebApplicationFactory();
        var client = factory.CreateClient();

        var ticket = await OpenReturnTicketAsync(client, customerId: "101", orderId: "1001");

        var first = await client.PostAsJsonAsync(
            $"/api/v1/internal/tickets/{ticket!.Id}/refund-decision",
            new { decision = "approve", source = "operator", reason = "ok" });
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        var second = await client.PostAsJsonAsync(
            $"/api/v1/internal/tickets/{ticket!.Id}/refund-decision",
            new { decision = "approve", source = "operator", reason = "duplicate" });
        second.StatusCode.Should().Be(HttpStatusCode.OK);

        var orderResp = await client.GetAsync("/api/v1/orders/1001");
        var order = await orderResp.Content.ReadFromJsonAsync<Order>();
        order!.Status.Should().Be("returned",
            "a duplicate approve must not flip the order back into a wrong state");
    }

    // ---------- customer-driven cancel reverts the order ----------

    [Fact]
    public async Task PatchTicket_CancelReturn_RevertsOrderToDelivered()
    {
        await using var factory = new CrmApiWebApplicationFactory();
        var client = factory.CreateClient();

        var ticket = await OpenReturnTicketAsync(client, customerId: "101", orderId: "1001");

        // Customer changes their mind.
        var patch = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/tickets/{ticket!.Id}")
        {
            Content = JsonContent.Create(new { status = "cancelled", customer_id = "101" })
        };
        patch.Headers.Add("X-Customer-Entra-Id", "101");
        var response = await client.SendAsync(patch);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Order must revert so the customer can re-file later.
        var orderResp = await client.GetAsync("/api/v1/orders/1001");
        var order = await orderResp.Content.ReadFromJsonAsync<Order>();
        order!.Status.Should().Be("delivered",
            "cancelling the return ticket must free the order so the customer can re-file or move on");
    }

    [Fact]
    public async Task PatchTicket_CancelGeneralCategory_DoesNotMutateOrder()
    {
        // The revert is gated on category=return AND order in
        // "return-started". Cancelling a general-category ticket
        // attached to an order must NOT touch the order.
        await using var factory = new CrmApiWebApplicationFactory();
        var client = factory.CreateClient();

        // Create a non-return ticket linked to a delivered order.
        var createReq = new CreateTicketRequest
        {
            CustomerId = "101",
            OrderId = "1001",
            Category = "general",
            Priority = "low",
            Subject = "Tracking question",
            Description = "When does this typically arrive?"
        };
        var createResp = await client.PostAsJsonAsync("/api/v1/tickets", createReq);
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var ticket = await createResp.Content.ReadFromJsonAsync<SupportTicket>();

        var patch = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/tickets/{ticket!.Id}")
        {
            Content = JsonContent.Create(new { status = "cancelled", customer_id = "101" })
        };
        patch.Headers.Add("X-Customer-Entra-Id", "101");
        var response = await client.SendAsync(patch);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var orderResp = await client.GetAsync("/api/v1/orders/1001");
        var order = await orderResp.Content.ReadFromJsonAsync<Order>();
        order!.Status.Should().Be("delivered",
            "general-category tickets must not influence the order's status");
    }

    // ---------- helpers ----------

    private static async Task<SupportTicket?> OpenReturnTicketAsync(
        HttpClient client, string customerId, string orderId)
    {
        var request = new CreateTicketRequest
        {
            CustomerId = customerId,
            OrderId = orderId,
            Category = "return",
            Priority = "medium",
            Subject = "Refund",
            Description = "Test return."
        };
        var response = await client.PostAsJsonAsync("/api/v1/tickets", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created,
            "the seed order should be in `delivered` status — fix the test seed if not");
        return await response.Content.ReadFromJsonAsync<SupportTicket>();
    }

    // ---------- prepaid return-label issuance ----------

    [Fact]
    public async Task CreateReturnTicket_StampsActiveReturnLabel()
    {
        // Happy path: filing a return on a delivered, in-window order
        // produces a ticket with all six label fields populated and
        // status="active".
        await using var factory = new CrmApiWebApplicationFactory();
        var client = factory.CreateClient();

        var ticket = await OpenReturnTicketAsync(client, customerId: "101", orderId: "1001");

        ticket!.ReturnLabelId.Should().NotBeNullOrWhiteSpace();
        ticket.ReturnLabelId.Should().StartWith("LBL-",
            "the issuer hands out LBL-prefixed ids");
        ticket.ReturnLabelCarrier.Should().Be("UPS");
        ticket.ReturnLabelUrl.Should().StartWith("https://example.com/return-labels/");
        ticket.ReturnLabelStatus.Should().Be("active");
        ticket.ReturnLabelCreatedAt.Should().NotBeNullOrWhiteSpace();
        ticket.ReturnLabelVoidedAt.Should().BeNull();
    }

    [Fact]
    public async Task CreateReturnTicket_NonReturnCategory_NoLabelStamped()
    {
        // Category=product-issue must NOT issue a label even on a
        // delivered, in-window order.
        await using var factory = new CrmApiWebApplicationFactory();
        var labels = new RecordingReturnLabelService();
        factory.ReturnLabelService = labels;
        var client = factory.CreateClient();

        var request = new CreateTicketRequest
        {
            CustomerId = "101",
            OrderId = "1001",
            Category = "product-issue",
            Priority = "low",
            Subject = "Defect",
            Description = "Has a hole."
        };
        var response = await client.PostAsJsonAsync("/api/v1/tickets", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        labels.CreateCalls.Should().BeEmpty(
            "only category=return should trigger a label issuance");
    }

    [Fact]
    public async Task CreateReturnTicket_LabelIssuanceFails_TicketStillCreatedWithFailedStatus()
    {
        // The carrier service is down. The ticket must still be created
        // (the customer's request is not lost) but the label status
        // surfaces "failed" so the UI / agent can explain the situation.
        await using var factory = new CrmApiWebApplicationFactory();
        factory.ReturnLabelService = new RecordingReturnLabelService
        {
            CreateThrows = (_, _) => new InvalidOperationException("carrier down")
        };
        var client = factory.CreateClient();

        var ticket = await OpenReturnTicketAsync(client, customerId: "101", orderId: "1001");

        ticket!.ReturnLabelId.Should().BeNull();
        ticket.ReturnLabelStatus.Should().Be("failed",
            "a failed carrier call must mark the label status so the UI can show it");
    }

    [Fact]
    public async Task CancelReturnTicket_VoidsLabelAtCarrier()
    {
        await using var factory = new CrmApiWebApplicationFactory();
        var labels = new RecordingReturnLabelService();
        factory.ReturnLabelService = labels;
        var client = factory.CreateClient();

        var ticket = await OpenReturnTicketAsync(client, customerId: "101", orderId: "1001");
        labels.CreateCalls.Should().HaveCount(1);

        var patch = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/tickets/{ticket!.Id}")
        {
            Content = JsonContent.Create(new { status = "cancelled" })
        };
        patch.Headers.Add("X-Customer-Entra-Id", "101");
        var response = await client.SendAsync(patch);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        labels.VoidCalls.Should().ContainSingle()
            .Which.Should().StartWith("LBL-",
                "cancelling a return must void the prepaid label at the carrier");

        var updated = await response.Content.ReadFromJsonAsync<SupportTicket>();
        updated!.ReturnLabelStatus.Should().Be("voided");
        updated.ReturnLabelVoidedAt.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task CancelReturnTicket_VoidThrows_TicketStaysOpen_ReturnsBadGateway()
    {
        // If the carrier void call fails, we MUST NOT cancel the ticket
        // — better to keep it open with an active label than leave the
        // customer holding an apparently-cancelled return whose label
        // is still live at the carrier.
        await using var factory = new CrmApiWebApplicationFactory();
        factory.ReturnLabelService = new RecordingReturnLabelService
        {
            VoidThrows = _ => new InvalidOperationException("carrier down")
        };
        var client = factory.CreateClient();

        var ticket = await OpenReturnTicketAsync(client, customerId: "101", orderId: "1001");

        var patch = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/tickets/{ticket!.Id}")
        {
            Content = JsonContent.Create(new { status = "cancelled" })
        };
        patch.Headers.Add("X-Customer-Entra-Id", "101");
        var response = await client.SendAsync(patch);

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway,
            "a carrier void failure must surface as a retryable gateway error");
    }

    [Fact]
    public async Task CancelNonReturnTicket_DoesNotCallVoid()
    {
        // Cancelling a non-return ticket (general/product-issue/shipping)
        // must NOT call the carrier — they don't have a label.
        await using var factory = new CrmApiWebApplicationFactory();
        var labels = new RecordingReturnLabelService();
        factory.ReturnLabelService = labels;
        var client = factory.CreateClient();

        var createReq = new CreateTicketRequest
        {
            CustomerId = "101",
            OrderId = "1001",
            Category = "general",
            Priority = "low",
            Subject = "Q",
            Description = "Q"
        };
        var createResp = await client.PostAsJsonAsync("/api/v1/tickets", createReq);
        var ticket = await createResp.Content.ReadFromJsonAsync<SupportTicket>();

        var patch = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/tickets/{ticket!.Id}")
        {
            Content = JsonContent.Create(new { status = "cancelled" })
        };
        patch.Headers.Add("X-Customer-Entra-Id", "101");
        await client.SendAsync(patch);

        labels.VoidCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task RefundApproved_LeavesLabelActive()
    {
        // Per user veto: approved refund does NOT mark the label "used".
        // A real carrier label is consumed at drop-off, not at merchant
        // approval, and we have no signal for that.
        await using var factory = new CrmApiWebApplicationFactory();
        var labels = new RecordingReturnLabelService();
        factory.ReturnLabelService = labels;
        var client = factory.CreateClient();

        var ticket = await OpenReturnTicketAsync(client, customerId: "101", orderId: "1001");

        var body = new { decision = "approve", source = "operator", reason = "ok" };
        var response = await client.PostAsJsonAsync(
            $"/api/v1/internal/tickets/{ticket!.Id}/refund-decision", body);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        labels.VoidCalls.Should().BeEmpty(
            "approved refund does not void or 'use' the label");
    }

    // ---------- 30-day return-window gate ----------

    [Fact]
    public async Task CreateReturnTicket_OutsideWindow_Returns409()
    {
        // Pin "today" to 2026-05-11 (the date the user reported the
        // bug). Order 1001 (Emma) was delivered 2026-03-08 = 64 days
        // ago — outside the 30-day window.
        await using var factory = new CrmApiWebApplicationFactory
        {
            TimeProvider = new FixedTimeProvider(
                new DateTimeOffset(2026, 5, 11, 12, 0, 0, TimeSpan.Zero))
        };
        var client = factory.CreateClient();

        var request = new CreateTicketRequest
        {
            CustomerId = "101",
            OrderId = "1001",
            Category = "return",
            Priority = "medium",
            Subject = "Late return",
            Description = "Forgot."
        };
        var response = await client.PostAsJsonAsync("/api/v1/tickets", request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("30 days");
        body.Should().Contain("ReturnWindowExpired");
    }

    [Fact]
    public async Task CreateReturnTicket_AtWindowBoundary_30Days_Allowed()
    {
        // Order 1001 delivered 2026-03-08. Today = 2026-04-07 → exactly
        // 30 days ago. Must be allowed (inclusive boundary).
        await using var factory = new CrmApiWebApplicationFactory
        {
            TimeProvider = new FixedTimeProvider(
                new DateTimeOffset(2026, 4, 7, 12, 0, 0, TimeSpan.Zero))
        };
        var client = factory.CreateClient();

        var request = new CreateTicketRequest
        {
            CustomerId = "101",
            OrderId = "1001",
            Category = "return",
            Priority = "medium",
            Subject = "Just in time",
            Description = "Right at the boundary."
        };
        var response = await client.PostAsJsonAsync("/api/v1/tickets", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            "the 30-day window is inclusive of day 30");
    }

    [Fact]
    public async Task CreateReturnTicket_AtWindowBoundary_31Days_Rejected()
    {
        // Order 1001 delivered 2026-03-08. Today = 2026-04-08 → 31 days
        // ago. Must be rejected.
        await using var factory = new CrmApiWebApplicationFactory
        {
            TimeProvider = new FixedTimeProvider(
                new DateTimeOffset(2026, 4, 8, 12, 0, 0, TimeSpan.Zero))
        };
        var client = factory.CreateClient();

        var request = new CreateTicketRequest
        {
            CustomerId = "101",
            OrderId = "1001",
            Category = "return",
            Priority = "medium",
            Subject = "One day late",
            Description = "Sorry."
        };
        var response = await client.PostAsJsonAsync("/api/v1/tickets", request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public void IsWithinWindow_NullDeliveryFallsBackToOrderDate()
    {
        var today = new DateOnly(2026, 5, 11);
        var result = Contoso.CrmApi.Services.ReturnEligibility.IsWithinWindow(
            estimatedDelivery: null,
            orderDate: "2026-05-01",
            today: today);
        result.IsEligible.Should().BeTrue();
        result.DaysSinceDelivery.Should().Be(10);
    }

    [Fact]
    public void IsWithinWindow_BothDatesNull_FailClosed()
    {
        var today = new DateOnly(2026, 5, 11);
        var result = Contoso.CrmApi.Services.ReturnEligibility.IsWithinWindow(
            estimatedDelivery: null,
            orderDate: null,
            today: today);
        result.IsEligible.Should().BeFalse();
        result.Reason.Should().Contain("can't determine");
    }

    [Fact]
    public void IsWithinWindow_FutureDelivery_AllowedAsZeroDays()
    {
        var today = new DateOnly(2026, 5, 11);
        var result = Contoso.CrmApi.Services.ReturnEligibility.IsWithinWindow(
            estimatedDelivery: "2026-05-15",
            orderDate: "2026-05-10",
            today: today);
        result.IsEligible.Should().BeTrue();
        result.DaysSinceDelivery.Should().Be(0);
    }
}
