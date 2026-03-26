using Contoso.CrmApi.Services;

namespace Contoso.CrmApi.Endpoints;

public static class PromotionEndpoints
{
    public static RouteGroupBuilder MapPromotionEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/promotions")
            .WithTags("Promotions");

        group.MapGet("/", async (ICosmosService cosmos, CancellationToken ct) =>
        {
            var promotions = await cosmos.GetAllPromotionsAsync(ct);
            return Results.Ok(promotions);
        })
        .WithName("GetAllPromotions")
        .WithSummary("List all active promotions");

        group.MapGet("/eligible/{customerId}", async (
            string customerId,
            ICosmosService cosmos,
            CancellationToken ct) =>
        {
            // Look up the customer to get their loyalty tier
            var customer = await cosmos.GetCustomerByIdAsync(customerId, ct);
            if (customer is null)
            {
                return Results.Problem(
                    detail: $"Customer with ID '{customerId}' was not found.",
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Not Found");
            }

            var promotions = await cosmos.GetEligiblePromotionsAsync(customer.LoyaltyTier, ct);
            return Results.Ok(promotions);
        })
        .WithName("GetEligiblePromotions")
        .WithSummary("Get promotions eligible for a customer based on their loyalty tier");

        return group;
    }
}
