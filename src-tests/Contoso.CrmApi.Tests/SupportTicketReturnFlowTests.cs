using System.Net;
using System.Net.Http.Json;
using Contoso.CrmApi.Models;
using FluentAssertions;

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
//         create return ticket   → order: delivered → return-requested
//         approve / below_thr.   → order: return-requested → returned
//         customer cancels ticket → order: return-requested → delivered
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
    public async Task CreateTicket_Return_OnDeliveredOrder_FlipsOrderToReturnRequested()
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
        order!.Status.Should().Be("return-requested",
            "the order should mirror the open return so the customer's UI doesn't lie");
    }

    [Fact]
    public async Task CreateTicket_Return_OnAlreadyReturnRequestedOrder_Returns409Conflict()
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
        body.Should().Contain("already been requested",
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
    public async Task RefundDecision_Reject_LeavesOrderInReturnRequested()
    {
        // Per the existing ApplyDecisionToTicket contract, reject leaves
        // the ticket in "open" status. Therefore the order also stays
        // in "return-requested" until the customer cancels the ticket.
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
        order!.Status.Should().Be("return-requested",
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
        // "return-requested". Cancelling a general-category ticket
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
}
