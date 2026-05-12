namespace Contoso.CrmApi.Services;

// Pure helper for the 30-day return-window check. Lives outside
// SupportTicketEndpoints so the gate can be unit-tested without
// spinning a WebApplicationFactory.
//
// DESIGN: fail-closed. If we cannot determine WHEN the order was
// delivered (no estimated_delivery, no order_date, both unparseable),
// we refuse the return rather than open a silent loophole. A delivered
// order whose dates are both junk is a data-quality issue \u2014 we surface
// it to the customer as "we can't confirm delivery date" rather than
// quietly approving.
//
// We compare DateOnly values to avoid timezone drift: the wire format
// is "yyyy-MM-dd" and is treated as a calendar date in UTC.
public static class ReturnEligibility
{
    public const int WindowDays = 30;

    public static EligibilityResult IsWithinWindow(
        string? estimatedDelivery,
        string? orderDate,
        DateOnly today,
        int windowDays = WindowDays)
    {
        // Prefer the carrier's estimated-delivery date (closer to the
        // policy's "date of delivery"). Fall back to order date so a
        // legacy row missing estimated_delivery still gets evaluated.
        var deliveredOn = TryParseDate(estimatedDelivery) ?? TryParseDate(orderDate);
        if (deliveredOn is null)
        {
            return new EligibilityResult(
                IsEligible: false,
                DaysSinceDelivery: null,
                Reason: "We can't determine when this order was delivered, so we can't confirm it's within the 30-day return window.");
        }

        var days = today.DayNumber - deliveredOn.Value.DayNumber;

        // Negative diff = delivery date is in the future relative to
        // "today" (clock skew / pre-dated test fixture). Treat that as
        // within the window so we don't punish a fast-shipping edge.
        if (days < 0)
        {
            return new EligibilityResult(IsEligible: true, DaysSinceDelivery: 0, Reason: null);
        }

        if (days > windowDays)
        {
            return new EligibilityResult(
                IsEligible: false,
                DaysSinceDelivery: days,
                Reason: $"Returns must be initiated within {windowDays} days of delivery. This order was delivered {days} days ago.");
        }

        return new EligibilityResult(IsEligible: true, DaysSinceDelivery: days, Reason: null);
    }

    private static DateOnly? TryParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        return DateOnly.TryParseExact(
            value, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var result)
            ? result
            : null;
    }
}

public sealed record EligibilityResult(bool IsEligible, int? DaysSinceDelivery, string? Reason);
