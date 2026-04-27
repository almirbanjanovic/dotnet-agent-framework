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
            ICosmosService cosmos,
            CancellationToken ct) =>
        {
            var tickets = await cosmos.GetTicketsByCustomerIdAsync(id, open_only ?? false, ct);
            return Results.Ok(tickets);
        })
        .WithName("GetCustomerTickets")
        .WithSummary("Get support tickets for a customer");

        group.MapPost("/tickets", async (
            CreateTicketRequest request,
            ICosmosService cosmos,
            CancellationToken ct) =>
        {
            var errors = ValidateCreateTicketRequest(request);
            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            var ticket = new SupportTicket
            {
                CustomerId = request.CustomerId,
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

    private static Dictionary<string, string[]> ValidateCreateTicketRequest(CreateTicketRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.CustomerId))
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
