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

    // Wire values fraud-workflow may post to /internal/tickets/{id}/refund-decision.
    // Maps roughly to FinalAction.Decision + Source on the workflow side.
    private static readonly string[] s_validRefundDecisions =
        ["approve", "reject", "below_threshold", "timeout"];

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
            IReturnLabelService returnLabelService,
            TimeProvider timeProvider,
            IServiceScopeFactory scopeFactory,
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
            //
            // Plus: order-state eligibility gate. A return only makes
            // sense once the customer actually has the package in hand.
            // Without this gate the agent will happily file a return on
            // a Shipped/Processing order, the workflow will approve it,
            // and the order's status will hang in limbo. Refusing on
            // the server keeps the LLM honest and matches the policy
            // documented to customers.
            Order? returnOrderToFlip = null;
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

                // Eligibility gate. 409 Conflict (not 400) — the request
                // is well-formed; the order is just in the wrong state.
                // The message is customer-readable because it bubbles
                // up through the agent verbatim.
                if (!string.Equals(order.Status, "delivered", StringComparison.OrdinalIgnoreCase))
                {
                    var humanReason = string.Equals(order.Status, "return-started", StringComparison.OrdinalIgnoreCase)
                        ? "A return has already been started for this order. Cancel the existing return ticket before filing a new one."
                        : string.Equals(order.Status, "returned", StringComparison.OrdinalIgnoreCase)
                            ? "This order has already been returned."
                            : $"Returns can only be filed once the order has been delivered. Current status: {order.Status}.";
                    return Results.Conflict(new
                    {
                        error = "OrderNotReturnable",
                        message = humanReason,
                        order_status = order.Status
                    });
                }

                // 30-day return window gate. The policy doc states
                // "Contoso Outdoors accepts returns within 30 calendar
                // days from the date of delivery." Without this gate the
                // agent would happily file a return on a months-old
                // order (Emma Wilson reported this against a March order
                // in May). We use the carrier's estimated_delivery as
                // the closest proxy for the actual delivery date and
                // fall back to order_date if missing. The helper
                // returns a customer-readable message; surface it
                // verbatim so the agent reads it back as-is.
                var eligibility = ReturnEligibility.IsWithinWindow(
                    order.EstimatedDelivery,
                    order.OrderDate,
                    DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime));
                if (!eligibility.IsEligible)
                {
                    return Results.Conflict(new
                    {
                        error = "ReturnWindowExpired",
                        message = eligibility.Reason!,
                        order_status = order.Status,
                        days_since_delivery = eligibility.DaysSinceDelivery,
                        window_days = ReturnEligibility.WindowDays
                    });
                }

                returnOrderToFlip = order;
            }

            var ticket = new SupportTicket
            {
                // Generate the id in the composition layer so both the
                // in-memory and Cosmos backends agree (Cosmos requires
                // `id` on create; the in-memory backend now respects it
                // when supplied). The new fraud-workflow loop closure
                // depends on a stable, non-empty Id reaching the callback.
                Id = $"ST-{Guid.NewGuid():N}",
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

            // Flip the order to "return-started" so the customer's
            // /orders view immediately reflects the open return. Reverts
            // to "delivered" if the customer cancels the ticket; flips
            // to "returned" on workflow approval. We use the same
            // `order` instance we already loaded for the eligibility
            // gate (no second round-trip), and we tolerate failure —
            // the ticket itself is the source of truth, the order
            // status is best-effort UX. Loud log on failure.
            if (returnOrderToFlip is not null)
            {
                returnOrderToFlip.Status = "return-started";
                try
                {
                    await cosmos.UpdateOrderAsync(returnOrderToFlip, ct);
                }
                catch (Exception ex)
                {
                    loggerFactory
                        .CreateLogger("Contoso.CrmApi.SupportTickets")
                        .LogWarning(ex,
                            "Failed to flip order {OrderId} to return-started after creating ticket {TicketId}.",
                            returnOrderToFlip.Id, created.Id);
                }
            }

            // Issue a prepaid return shipping label. The fake impl is
            // synchronous + offline; a real impl (Shippo, EasyPost,
            // carrier API) would do an outbound call here. Two failure
            // modes are handled explicitly:
            //
            //   1. CreateAsync throws → no label exists at the carrier;
            //      stamp ReturnLabelStatus="failed" so the UI surfaces
            //      the situation rather than silently leaving a return
            //      ticket with no label. Customer can cancel and retry.
            //
            //   2. CreateAsync succeeds but the follow-up UpdateTicket
            //      to stamp the fields throws → carrier holds an
            //      orphaned label with no DB reference. Compensate
            //      immediately by VoidAsync. If the void ALSO throws,
            //      log the orphan id loudly so ops can reconcile.
            //
            // Persistence and the carrier call after eligibility passes
            // intentionally use CancellationToken.None so a client
            // disconnect mid-creation cannot leave us with a created
            // label and an unstamped ticket.
            if (string.Equals(created.Category, "return", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(created.OrderId))
            {
                var ticketLogger = loggerFactory.CreateLogger("Contoso.CrmApi.SupportTickets");
                ReturnLabel? issuedLabel = null;
                try
                {
                    issuedLabel = await returnLabelService.CreateAsync(
                        created.Id, created.OrderId!, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    ticketLogger.LogWarning(ex,
                        "Failed to issue return label for ticket {TicketId} (order {OrderId}).",
                        created.Id, created.OrderId);
                    created.ReturnLabelStatus = "failed";
                    try
                    {
                        await cosmos.UpdateTicketAsync(created, CancellationToken.None);
                    }
                    catch (Exception stampEx)
                    {
                        ticketLogger.LogWarning(stampEx,
                            "Failed to stamp return_label_status=failed on ticket {TicketId}.", created.Id);
                    }
                }

                if (issuedLabel is not null)
                {
                    created.ReturnLabelId = issuedLabel.Id;
                    created.ReturnLabelCarrier = issuedLabel.Carrier;
                    created.ReturnLabelUrl = issuedLabel.Url;
                    created.ReturnLabelStatus = "active";
                    created.ReturnLabelCreatedAt = issuedLabel.CreatedAt;
                    try
                    {
                        await cosmos.UpdateTicketAsync(created, CancellationToken.None);
                    }
                    catch (Exception stampEx)
                    {
                        ticketLogger.LogError(stampEx,
                            "Stamping return label {LabelId} on ticket {TicketId} failed; attempting carrier-side void to avoid orphan.",
                            issuedLabel.Id, created.Id);
                        try
                        {
                            await returnLabelService.VoidAsync(issuedLabel.Id, CancellationToken.None);
                        }
                        catch (Exception voidEx)
                        {
                            // Loud log: ORPHAN LABEL at the carrier with no
                            // DB record. Operator must reconcile manually.
                            ticketLogger.LogError(voidEx,
                                "ORPHAN LABEL {LabelId} (ticket {TicketId}, order {OrderId}) — both stamp and compensating void failed.",
                                issuedLabel.Id, created.Id, created.OrderId);
                        }
                        // Reset in-memory fields so the response we return
                        // doesn't claim a label that isn't stamped.
                        created.ReturnLabelId = null;
                        created.ReturnLabelCarrier = null;
                        created.ReturnLabelUrl = null;
                        created.ReturnLabelStatus = "failed";
                        created.ReturnLabelCreatedAt = null;
                        // Persist the failed marker too, best-effort, so a
                        // later GET reflects the same status the response
                        // body claims (Adv-review A4). If THIS upsert also
                        // fails the ticket is left without a stamp — not
                        // worse than before this patch, and already logged.
                        try
                        {
                            await cosmos.UpdateTicketAsync(created, CancellationToken.None);
                        }
                        catch (Exception persistEx)
                        {
                            ticketLogger.LogWarning(persistEx,
                                "Failed to persist return_label_status=failed on ticket {TicketId} after compensating void.",
                                created.Id);
                        }
                    }
                }
            }

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
            //
            // Adv-review A1: a return whose label issuance FAILED is not
            // actionable — the customer literally has no way to ship the
            // item back. Skip the refund-risk run so ops doesn't auto-
            // resolve the ticket and flip the order to "returned" while
            // the customer is still waiting for a manually-issued label.
            // The ticket stays open (status="open", label_status="failed")
            // and an operator can re-issue the label or take over.
            if (string.Equals(created.Category, "return", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(created.OrderId) &&
                !string.Equals(created.ReturnLabelStatus, "failed", StringComparison.OrdinalIgnoreCase))
            {
                // Resolve a fresh scope inside the background task —
                // ICosmosService is scoped in Cosmos mode, so capturing
                // the request-scoped instance would race with the
                // request scope's disposal. (Adversarial-review A7.)
                _ = TriggerRefundAlertAsync(
                    scopeFactory, loggerFactory,
                    created.Id!, created.OrderId!, created.CustomerId, created.Description);
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
            IReturnLabelService returnLabelService,
            ILoggerFactory loggerFactory,
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

            var ticketLogger = loggerFactory.CreateLogger("Contoso.CrmApi.SupportTickets");
            var willCancelReturn =
                string.Equals(existing.Category, "return", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(request.Status, "cancelled", StringComparison.OrdinalIgnoreCase);

            // Cancel-path label void. Order matters: void at the
            // carrier FIRST, then update the ticket. If the void
            // throws, return 502 so the customer can retry — leaving
            // the ticket open with an active label is correct because
            // a cancelled ticket whose label is still active at the
            // carrier is the worse failure mode (lost merchandise,
            // unexpected return shipping cost). Already-voided labels
            // (or labels that never existed because issuance failed
            // at create time) are skipped without an external call.
            string? voidedAt = null;
            if (willCancelReturn &&
                !string.IsNullOrWhiteSpace(existing.ReturnLabelId) &&
                string.Equals(existing.ReturnLabelStatus, "active", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    await returnLabelService.VoidAsync(existing.ReturnLabelId!, CancellationToken.None);
                    voidedAt = DateTimeOffset.UtcNow.ToString(
                        "yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);
                }
                catch (Exception ex)
                {
                    ticketLogger.LogError(ex,
                        "Failed to void return label {LabelId} for ticket {TicketId}; cancellation will not proceed.",
                        existing.ReturnLabelId, existing.Id);
                    return Results.Json(new
                    {
                        error = "ReturnLabelVoidFailed",
                        message = "We couldn't void the return shipping label. Please try cancelling again in a moment."
                    }, statusCode: StatusCodes.Status502BadGateway);
                }
            }

            existing.Status = request.Status.ToLowerInvariant();
            existing.ClosedAt = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd");
            if (voidedAt is not null)
            {
                existing.ReturnLabelStatus = "voided";
                existing.ReturnLabelVoidedAt = voidedAt;
            }
            var updated = await cosmos.UpdateTicketAsync(existing, ct);

            // If the customer just cancelled a return ticket, free the
            // order from "return-started" so they can re-file later.
            // Best-effort with a strict require-from guard — we never
            // trample a "returned" or "delivered" state set elsewhere.
            // Resolved (the other allowed customer-set status) does NOT
            // revert, because the workflow's approval path drives
            // "returned".
            if (willCancelReturn && !string.IsNullOrWhiteSpace(existing.OrderId))
            {
                await TryFlipOrderStatusAsync(
                    cosmos, existing.OrderId!, targetStatus: "delivered",
                    requireFromAny: ["return-started"], ct,
                    ticketLogger);
            }

            return Results.Ok(updated);
        })
        .WithName("UpdateTicketStatus")
        .WithSummary("Update a support ticket's status (cancel or resolve)");

        // POST /api/v1/internal/tickets/{id}/refund-decision — service callback.
        //
        // Called by fraud-workflow when a refund decision is reached
        // (auto-approve, operator approve/reject, timeout) so the
        // customer-facing ticket reflects the outcome. The path lives
        // under /internal/ specifically so it is NOT reverse-proxied by
        // the BFF — only services on the cluster network can hit it.
        //
        // Identity model: same as every other CRM API endpoint — relies
        // on network isolation (cluster networking + non-public ingress)
        // for trust. The endpoint deliberately does NOT require the
        // X-Customer-Entra-Id header because it is a service callback,
        // not a customer action; the calling service supplies the
        // canonical customerId via path lookup, not a header claim.
        group.MapPost("/internal/tickets/{id}/refund-decision", async (
            string id,
            RefundDecisionRequest request,
            ICosmosService cosmos,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("Contoso.CrmApi.SupportTickets.RefundDecisionCallback");

            if (string.IsNullOrWhiteSpace(request.Decision) ||
                !s_validRefundDecisions.Contains(request.Decision, StringComparer.OrdinalIgnoreCase))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["decision"] = [$"decision must be one of: {string.Join(", ", s_validRefundDecisions)}."]
                });
            }

            // We don't know the customer up-front (this is a system callback)
            // so we have to scan via UpdateTicketAsync's helper which doesn't
            // exist yet. Use the dedicated lookup that bypasses owner check.
            var existing = await cosmos.GetTicketByIdInternalAsync(id, ct);
            if (existing is null)
            {
                logger.LogWarning(
                    "Refund-decision callback for unknown ticket {TicketId} (alert {AlertId}).",
                    id, request.AlertId);
                // 404 is intentional — fraud-workflow uses it to detect
                // tickets that were cancelled by the customer mid-review.
                return Results.NotFound();
            }

            // Idempotency: if the ticket is already resolved/cancelled,
            // do NOT mutate the status further. Still append the comment
            // so the audit trail shows the late callback arrived.
            var priorTicketStatus = existing.Status;
            ApplyDecisionToTicket(existing, request);

            var updated = await cosmos.UpdateTicketAsync(existing, ct);

            // Mirror the refund decision onto the order. Only flip when
            // THIS call transitioned the ticket open→resolved (so a
            // duplicate callback doesn't double-mutate). Reject/timeout
            // intentionally do NOT touch the order — per the existing
            // ApplyDecisionToTicket contract those leave the ticket
            // open and the order stays in `return-started` until the
            // customer cancels or a follow-up approval lands.
            //
            // Self-heal: accept either "return-started" (the normal
            // post-create state) OR "delivered" (the recovery state
            // where the create-time flip silently failed). An approved
            // refund must always land the order in "returned". (Adv-
            // review R1 #2.)
            var didCloseTicket =
                string.Equals(priorTicketStatus, "open", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(existing.Status, "open", StringComparison.OrdinalIgnoreCase);
            var isApproval =
                string.Equals(request.Decision, "approve", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(request.Decision, "below_threshold", StringComparison.OrdinalIgnoreCase);
            if (didCloseTicket && isApproval && !string.IsNullOrWhiteSpace(existing.OrderId))
            {
                await TryFlipOrderStatusAsync(
                    cosmos, existing.OrderId!, targetStatus: "returned",
                    requireFromAny: ["return-started", "delivered"], ct, logger);
            }

            logger.LogInformation(
                "Applied refund decision {Decision} (source {Source}) to ticket {TicketId} (alert {AlertId}).",
                request.Decision, request.Source, id, request.AlertId);

            return Results.Ok(updated);
        })
        .WithName("ApplyRefundDecision")
        .WithSummary("Service callback from fraud-workflow to update a ticket on a refund decision");

        return group;
    }

    // Background helper: looks up the order to learn the refund amount
    // (the fraud-workflow needs `amount` to gate against its threshold)
    // and fires a refund alert. Errors are swallowed and logged — see
    // the FraudWorkflowClient for the rationale.
    //
    // When fraud-workflow short-circuits with status="below_threshold"
    // we close the loop here directly: the customer-facing ticket is
    // resolved with an audit comment so the customer sees a real
    // outcome instead of an open ticket sitting forever. For above-
    // threshold alerts the workflow runs and calls back into
    // /api/v1/internal/tickets/{id}/refund-decision once a terminal
    // decision is reached.
    //
    // Lifetime: spins up its OWN DI scope so we can resolve scoped
    // services (ICosmosService is Scoped in Cosmos mode) safely after
    // the request that triggered us has gone away.
    private static async Task TriggerRefundAlertAsync(
        IServiceScopeFactory scopeFactory,
        ILoggerFactory loggerFactory,
        string ticketId,
        string orderId,
        string customerId,
        string reason)
    {
        var logger = loggerFactory.CreateLogger("Contoso.CrmApi.SupportTickets.RefundAlert");
        try
        {
            using var scope = scopeFactory.CreateScope();
            var cosmos = scope.ServiceProvider.GetRequiredService<ICosmosService>();
            var fraudWorkflow = scope.ServiceProvider.GetRequiredService<FraudWorkflowClient>();

            // Fresh CT — caller's request CT is gone by the time we run.
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var order = await cosmos.GetOrderByIdAsync(orderId, cts.Token);
            if (order is null)
            {
                logger.LogWarning(
                    "Skipped refund alert for ticket on order {OrderId}: order not found.", orderId);
                return;
            }

            var resolvedReason = string.IsNullOrWhiteSpace(reason)
                ? "Customer-initiated return request."
                : reason;

            var outcome = await fraudWorkflow.SubmitRefundAlertAsync(
                customerId,
                orderId,
                order.TotalAmount,
                resolvedReason,
                ticketId,
                cts.Token);

            if (outcome.Status == FraudWorkflowResponseStatus.BelowThreshold)
            {
                // Close the customer's loop directly. The workflow won't
                // call back for sub-threshold alerts (it didn't even run
                // the agents) so we have to apply the resolution here.
                await ResolveTicketAsBelowThresholdAsync(cosmos, ticketId, order.TotalAmount, cts.Token, logger);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Refund-alert trigger failed for customer {CustomerId} order {OrderId}.",
                customerId, orderId);
        }
    }

    // Customer-visible auto-approval for sub-threshold refunds. Looks up
    // the ticket without an owner check (we are the system) and applies
    // a synthetic FinalAction equivalent so the audit trail matches what
    // the workflow would have written for an above-threshold approval.
    private static async Task ResolveTicketAsBelowThresholdAsync(
        ICosmosService cosmos,
        string ticketId,
        decimal amount,
        CancellationToken ct,
        ILogger logger)
    {
        var ticket = await cosmos.GetTicketByIdInternalAsync(ticketId, ct);
        if (ticket is null)
        {
            logger.LogWarning(
                "Below-threshold short-circuit for unknown ticket {TicketId}.", ticketId);
            return;
        }

        // Don't trample a customer cancel that landed first — only mutate
        // status if the ticket is still open. Either way append the
        // audit line so the late callback is visible.
        var priorStatus = ticket.Status;
        var request = new RefundDecisionRequest
        {
            Decision = "below_threshold",
            Source = "system",
            Reason = $"Refund amount ${amount:F2} is below the risk-review threshold; auto-approved."
        };
        ApplyDecisionToTicket(ticket, request);
        await cosmos.UpdateTicketAsync(ticket, ct);

        // Mirror onto the order if this call actually closed the ticket
        // (open → resolved). Same self-heal as the callback path: accept
        // both "return-started" and "delivered" so a missed create-
        // time flip still ends in "returned".
        if (!string.IsNullOrWhiteSpace(ticket.OrderId) &&
            string.Equals(priorStatus, "open", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(ticket.Status, "open", StringComparison.OrdinalIgnoreCase))
        {
            await TryFlipOrderStatusAsync(
                cosmos, ticket.OrderId!, targetStatus: "returned",
                requireFromAny: ["return-started", "delivered"], ct, logger);
        }

        logger.LogInformation(
            "Auto-resolved ticket {TicketId} as below-threshold (amount ${Amount}).",
            ticketId, amount);
    }

    // Pure status + comment mutation. Tested directly so the wiring
    // doesn't accidentally accept a status transition that the customer
    // PATCH endpoint would reject.
    internal static void ApplyDecisionToTicket(SupportTicket ticket, RefundDecisionRequest request)
    {
        // Sanitize every field that lands in the audit line so a forged
        // caller can't inject fake bracketed entries or break the
        // append-only format. Cap length to keep the comments column
        // bounded for the CSV/Cosmos round trip. (Adversarial-review A6/B1.)
        var safeReason = SanitizeForAuditLine(request.Reason);
        var safeSource = NormalizeSource(request.Source);
        var alertSuffix = string.IsNullOrWhiteSpace(request.AlertId)
            ? string.Empty
            : $" (alert {SanitizeForAuditLine(request.AlertId)})";
        var line = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "[{0:yyyy-MM-ddTHH:mm:ssZ} {1}/{2}] {3}{4}",
            DateTimeOffset.UtcNow,
            safeSource,
            request.Decision.ToLowerInvariant(),
            string.IsNullOrWhiteSpace(safeReason) ? "(no reason given)" : safeReason,
            alertSuffix);

        ticket.Comments = string.IsNullOrWhiteSpace(ticket.Comments)
            ? line
            : ticket.Comments + "\n" + line;

        // Status transitions:
        //   approve / below_threshold → resolved (the refund happened)
        //   reject  / timeout         → stays open (customer must follow up)
        // Idempotency: don't reopen a ticket that was already cancelled
        // by the customer mid-review.
        if (!string.Equals(ticket.Status, "open", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (string.Equals(request.Decision, "approve", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(request.Decision, "below_threshold", StringComparison.OrdinalIgnoreCase))
        {
            ticket.Status = "resolved";
            ticket.ClosedAt = DateTimeOffset.UtcNow.ToString(
                "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        }
        // For "reject" and "timeout" we deliberately leave status="open"
        // so the customer sees the ticket is still being worked. The
        // appended comment surfaces the reason on the /tickets page.
    }

    // Best-effort order-status mutation used by the return / refund flow.
    // Refuses to mutate when the order is not in any of the expected
    // source states (so a concurrent customer-cancel or a duplicate
    // refund callback can't trample a downstream state). Swallows + logs
    // all exceptions — the ticket is the source of truth, the order's
    // status is a UX mirror.
    //
    // `requireFromAny` lists the acceptable current statuses. Pass more
    // than one to enable self-healing — e.g. the approval path accepts
    // BOTH "return-started" (the normal post-create state) AND
    // "delivered" (the recovery state where the create-time flip
    // failed silently). That guarantees an approved refund always lands
    // the order in "returned" regardless of which intermediate writes
    // succeeded.
    internal static async Task TryFlipOrderStatusAsync(
        ICosmosService cosmos,
        string orderId,
        string targetStatus,
        string[] requireFromAny,
        CancellationToken ct,
        ILogger logger)
    {
        try
        {
            var order = await cosmos.GetOrderByIdAsync(orderId, ct);
            if (order is null)
            {
                logger.LogWarning(
                    "Skipped order-status flip: order {OrderId} not found.", orderId);
                return;
            }
            var matches = requireFromAny.Any(s =>
                string.Equals(order.Status, s, StringComparison.OrdinalIgnoreCase));
            if (!matches)
            {
                // Not an error: a concurrent path (customer cancel,
                // duplicate callback, manual reset) already moved the
                // order. Leave it alone.
                logger.LogInformation(
                    "Skipped order-status flip for {OrderId}: expected one of [{From}] but found '{Current}' (target was '{Target}').",
                    orderId, string.Join(",", requireFromAny), order.Status, targetStatus);
                return;
            }
            // Idempotency: already at the target — nothing to do, no log noise.
            if (string.Equals(order.Status, targetStatus, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            var fromStatus = order.Status;
            order.Status = targetStatus;
            await cosmos.UpdateOrderAsync(order, ct);
            logger.LogInformation(
                "Flipped order {OrderId} from '{From}' to '{Target}'.",
                orderId, fromStatus, targetStatus);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to flip order {OrderId} status to '{Target}'.",
                orderId, targetStatus);
        }
    }

    // Allowlist of trusted callers. Anything else (including null) is
    // normalized to "system" so a forged callback can't sneak unexpected
    // tokens into the audit prefix.
    private static readonly HashSet<string> s_validSources = new(StringComparer.OrdinalIgnoreCase)
    {
        "auto", "operator", "timeout", "system"
    };

    private static string NormalizeSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return "system";
        }
        var lower = source.Trim().ToLowerInvariant();
        return s_validSources.Contains(lower) ? lower : "system";
    }

    private static string SanitizeForAuditLine(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        // Strip control chars and collapse whitespace runs to a single
        // space. Replace square brackets with parentheses so a hostile
        // caller can't forge a fake `[YYYY-... source/decision]` prefix
        // inside their reason text. Keep the result printable so a JSON
        // round trip won't produce escaped sequences in the audit line.
        var trimmed = input.Trim();
        var builder = new System.Text.StringBuilder(trimmed.Length);
        var lastWasSpace = false;
        foreach (var ch in trimmed)
        {
            if (char.IsControl(ch) || ch == '\n' || ch == '\r' || ch == '\t')
            {
                if (!lastWasSpace)
                {
                    builder.Append(' ');
                    lastWasSpace = true;
                }
                continue;
            }
            // Defang anything that looks like an audit-line opener.
            var safe = ch switch
            {
                '[' => '(',
                ']' => ')',
                _ => ch
            };
            builder.Append(safe);
            lastWasSpace = char.IsWhiteSpace(safe);
        }

        const int MaxLineLength = 500;
        var result = builder.ToString();
        return result.Length > MaxLineLength
            ? result[..MaxLineLength] + "…"
            : result;
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
