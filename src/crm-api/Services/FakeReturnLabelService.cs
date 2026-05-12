using Microsoft.Extensions.Logging;

namespace Contoso.CrmApi.Services;

// Local-dev / lab fake. Generates a synthetic label id (`LBL-<12hex>`),
// pins the carrier to "UPS" so demo screenshots stay deterministic,
// and returns a non-clickable example.com URL. The UI deliberately
// renders the URL as plain text \u2014 see SECURITY note on
// IReturnLabelService.
//
// Never throws on the success path so test scenarios that need to
// observe a failure must register their own throwing impl. VoidAsync
// is idempotent: voiding a label that was never issued logs a debug
// line and returns; voiding the same label twice is a no-op.
//
// Stateless aside from the logger \u2014 safe to register as a singleton.
public sealed class FakeReturnLabelService : IReturnLabelService
{
    private readonly ILogger<FakeReturnLabelService> _logger;
    private readonly TimeProvider _timeProvider;

    public FakeReturnLabelService(
        ILogger<FakeReturnLabelService> logger,
        TimeProvider timeProvider)
    {
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public Task<ReturnLabel> CreateAsync(string ticketId, string orderId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // 12 hex chars is plenty of entropy for a demo and keeps the
        // label id short enough to read aloud in lab walkthroughs.
        var labelId = $"LBL-{Guid.NewGuid():N}"[..16];
        var url = $"https://example.com/return-labels/{labelId}.pdf";
        var createdAt = _timeProvider.GetUtcNow().ToString(
            "yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);

        _logger.LogInformation(
            "Issued fake return label {LabelId} for ticket {TicketId} on order {OrderId}.",
            labelId, ticketId, orderId);

        return Task.FromResult(new ReturnLabel(labelId, "UPS", url, createdAt));
    }

    public Task VoidAsync(string labelId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _logger.LogInformation("Voided fake return label {LabelId}.", labelId);
        return Task.CompletedTask;
    }
}
