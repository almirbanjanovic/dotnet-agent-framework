using Contoso.CrmApi.Models;
using Contoso.CrmApi.Services;

namespace Contoso.CrmApi.Endpoints;

public static class SupportTicketEndpoints
{
    private static readonly string[] s_validCategories = ["shipping", "product-issue", "return", "general"];
    private static readonly string[] s_validPriorities = ["low", "medium", "high"];

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
            return Results.Created($"/api/v1/tickets/{created.Id}", created);
        })
        .WithName("CreateTicket")
        .WithSummary("Create a new support ticket");

        return group;
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
