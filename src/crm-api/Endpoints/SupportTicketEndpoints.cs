using Contoso.CrmApi.Models;
using Contoso.CrmApi.Services;

namespace Contoso.CrmApi.Endpoints;

public static class SupportTicketEndpoints
{
    private static readonly string[] s_validCategories = ["shipping", "product-issue", "return", "general"];
    private static readonly string[] s_validPriorities = ["low", "medium", "high"];

    // Statuses a customer (or the agent on their behalf) is allowed to
    // transition a ticket INTO via PATCH /tickets/{id}. Anything else —
    // notably "open", "in_progress", "escalated" — is reserved for
    // server-side workflows. Mirrors the priority/category allow-lists.
    private static readonly string[] s_customerSettableStatuses = ["cancelled", "resolved"];

    public static RouteGroupBuilder MapSupportTicketEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1")
            .WithTags("Support Tickets");

        group.MapGet("/customers/{id}/tickets", async (
            string id,
            bool? open_only,
            CustomerContext customerContext,
            ICosmosService cosmos,
            CancellationToken ct) =>
        {
            // Defense-in-depth: when the BFF forwards X-Customer-Entra-Id,
            // refuse to read another customer's tickets. Empty list (not
            // 403) so we don't leak the existence of other customer IDs
            // and the agent path doesn't error.
            if (!CustomerEndpoints.IsAuthorizedFor(customerContext, id))
            {
                return Results.Ok(Array.Empty<SupportTicket>());
            }

            var tickets = await cosmos.GetTicketsByCustomerIdAsync(id, open_only ?? false, ct);
            return Results.Ok(tickets);
        })
        .WithName("GetCustomerTickets")
        .WithSummary("Get support tickets for a customer");

        group.MapPost("/tickets", async (
            CreateTicketRequest request,
            CustomerContext customerContext,
            ICosmosService cosmos,
            FraudWorkflowClient fraudWorkflow,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            // SECURITY: prefer the X-Customer-Entra-Id header that the BFF
            // sets after JWT validation; only fall back to the body's
            // customer_id when the header is absent (legacy callers and
            // tests). When the header IS present and disagrees with the
            // body, the header wins — the body cannot be used to file a
            // ticket against another customer.
            //
            // The agent → CRM-MCP → CRM-API path also forwards this
            // header end-to-end via the `CustomerHeaderForwarder`
            // DelegatingHandler attached to each agent's named
            // IHttpClientFactory clients ("crm-mcp", "knowledge-mcp").
            // The LLM is therefore NOT the only thing preventing a
            // cross-customer ticket — when the header is present, the
            // server enforces it regardless of what the body says.
            var headerCustomerId = customerContext.GetCustomerEntraId();
            var customerId = string.IsNullOrWhiteSpace(headerCustomerId)
                ? request.CustomerId
                : headerCustomerId;

            var errors = ValidateCreateTicketRequest(request, customerId);
            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            // Cross-customer order-id guard (adversarial-review finding A2):
            // a `category=return` ticket targets a specific order, and
            // the downstream refund-alert carries (customerId, orderId,
            // amount). Without this check, a malicious caller could file
            // a return for someone ELSE's order — fraud-workflow would
            // then receive an alert with attacker customerId + victim
            // orderId + victim amount. Reject the request before
            // persisting the ticket.
            if (string.Equals(request.Category, "return", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(request.OrderId))
            {
                var order = await cosmos.GetOrderByIdAsync(request.OrderId!, ct);
                if (order is null ||
                    !string.Equals(order.CustomerId, customerId, StringComparison.OrdinalIgnoreCase))
                {
                    // 404 (not 403) so an attacker cannot enumerate which
                    // order ids exist or who owns them.
                    return Results.NotFound(new
                    {
                        error = "OrderNotFound",
                        message = $"Order '{request.OrderId}' was not found for this customer."
                    });
                }
            }

            var ticket = new SupportTicket
            {
                CustomerId = customerId,
                OrderId = request.OrderId,
                Category = request.Category,
                Subject = request.Subject,
                Description = request.Description,
                Status = "open",
                Priority = request.Priority,
                OpenedAt = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd"),
                ClosedAt = null
            };

            var created = await cosmos.CreateTicketAsync(ticket, ct);

            // Real-world wiring (was: only the dev "Simulate alert" button
            // on the Operations page could trigger a refund-risk run).
            // When the customer files a category=return ticket against an
            // existing order, fan the event out to fraud-workflow so the
            // refund-risk graph runs and the ops queue picks it up. We
            // run this DETACHED so an unresponsive workflow can't stall
            // the customer's ticket-creation response — the client gets
            // 201 immediately, the workflow trigger happens in the
            // background, and any failure is logged (not retried) since
            // ops can also start the same alert from the Operations page.
            if (string.Equals(created.Category, "return", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(created.OrderId))
            {
                _ = TriggerRefundAlertAsync(
                    cosmos, fraudWorkflow, loggerFactory, created.OrderId!, created.CustomerId, created.Description);
            }

            return Results.Created($"/api/v1/tickets/{created.Id}", created);
        })
        .WithName("CreateTicket")
        .WithSummary("Create a new support ticket");

        // PATCH /api/v1/tickets/{id} — customer-facing status update.
        // Used by the agent's `cancel_support_ticket` MCP tool and by the
        // /tickets page "Cancel" affordance. Owner-checked: returns 404
        // (not 403) when the caller is not the ticket owner so an
        // attacker can't probe for existing ticket ids.
        //
        // Identity model: this endpoint REQUIRES the X-Customer-Entra-Id
        // header. Real callers (BFF + crm-mcp) attach it via the
        // CustomerHeaderForwarder DelegatingHandler. We DO NOT honour a
        // body-supplied customer_id — any caller who reached us without
        // the header is either misconfigured or attempting to mutate
        // someone else's ticket. (Adversarial-review finding A1/B1.)
        group.MapPatch("/tickets/{id}", async (
            string id,
            UpdateTicketStatusRequest request,
            CustomerContext customerContext,
            ICosmosService cosmos,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Status) ||
                !s_customerSettableStatuses.Contains(request.Status, StringComparer.OrdinalIgnoreCase))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["status"] = [$"status must be one of: {string.Join(", ", s_customerSettableStatuses)}."]
                });
            }

            var customerId = customerContext.GetCustomerEntraId();
            if (string.IsNullOrWhiteSpace(customerId))
            {
                // No body fallback. Tests/local must propagate the
                // header. Returning 401 instead of 400 to make it
                // unambiguous that this is an authentication failure.
                return Results.Unauthorized();
            }

            var existing = await cosmos.GetTicketByIdAsync(id, customerId, ct);
            if (existing is null)
            {
                // Either the ticket doesn't exist OR it isn't owned by
                // this customer. Same response either way.
                return Results.NotFound();
            }

            // Idempotency: PATCH-ing to the current status is a no-op.
            if (string.Equals(existing.Status, request.Status, StringComparison.OrdinalIgnoreCase))
            {
                return Results.Ok(existing);
            }

            // Don't reopen an already-closed ticket via this endpoint.
            // Customers can only move open → cancelled/resolved.
            if (!string.Equals(existing.Status, "open", StringComparison.OrdinalIgnoreCase))
            {
                return Results.Conflict(new
                {
                    error = "InvalidStateTransition",
                    message = $"Ticket is already {existing.Status}; cannot transition to {request.Status}."
                });
            }

            existing.Status = request.Status.ToLowerInvariant();
            existing.ClosedAt = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd");
            var updated = await cosmos.UpdateTicketAsync(existing, ct);
            return Results.Ok(updated);
        })
        .WithName("UpdateTicketStatus")
        .WithSummary("Update a support ticket's status (cancel or resolve)");

        return group;
    }

    // Background helper: looks up the order to learn the refund amount
    // (the fraud-workflow needs `amount` to gate against its threshold)
    // and fires a refund alert. Errors are swallowed and logged — see
    // the FraudWorkflowClient for the rationale.
    private static async Task TriggerRefundAlertAsync(
        ICosmosService cosmos,
        FraudWorkflowClient fraudWorkflow,
        ILoggerFactory loggerFactory,
        string orderId,
        string customerId,
        string reason)
    {
        var logger = loggerFactory.CreateLogger("Contoso.CrmApi.SupportTickets.RefundAlert");
        try
        {
            // Fresh CT — caller's request CT is gone by the time we run.
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var order = await cosmos.GetOrderByIdAsync(orderId, cts.Token);
            if (order is null)
            {
                logger.LogWarning(
                    "Skipped refund alert for ticket on order {OrderId}: order not found.", orderId);
                return;
            }

            await fraudWorkflow.SubmitRefundAlertAsync(
                customerId,
                orderId,
                order.TotalAmount,
                string.IsNullOrWhiteSpace(reason) ? "Customer-initiated return request." : reason,
                cts.Token);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Refund-alert trigger failed for customer {CustomerId} order {OrderId}.",
                customerId, orderId);
        }
    }

    private static Dictionary<string, string[]> ValidateCreateTicketRequest(CreateTicketRequest request, string resolvedCustomerId)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(resolvedCustomerId))
        {
            errors["customer_id"] = ["customer_id is required."];
        }

        if (string.IsNullOrWhiteSpace(request.Category))
        {
            errors["category"] = ["category is required."];
        }
        else if (!s_validCategories.Contains(request.Category))
        {
            errors["category"] = [$"category must be one of: {string.Join(", ", s_validCategories)}."];
        }

        if (string.IsNullOrWhiteSpace(request.Priority))
        {
            errors["priority"] = ["priority is required."];
        }
        else if (!s_validPriorities.Contains(request.Priority))
        {
            errors["priority"] = [$"priority must be one of: {string.Join(", ", s_validPriorities)}."];
        }

        if (string.IsNullOrWhiteSpace(request.Subject))
        {
            errors["subject"] = ["subject is required."];
        }

        if (string.IsNullOrWhiteSpace(request.Description))
        {
            errors["description"] = ["description is required."];
        }

        return errors;
    }
}
