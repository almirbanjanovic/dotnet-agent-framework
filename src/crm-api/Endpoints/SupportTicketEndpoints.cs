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
            ApplyDecisionToTicket(existing, request);

            var updated = await cosmos.UpdateTicketAsync(existing, ct);

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
        var request = new RefundDecisionRequest
        {
            Decision = "below_threshold",
            Source = "system",
            Reason = $"Refund amount ${amount:F2} is below the risk-review threshold; auto-approved."
        };
        ApplyDecisionToTicket(ticket, request);
        await cosmos.UpdateTicketAsync(ticket, ct);

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
