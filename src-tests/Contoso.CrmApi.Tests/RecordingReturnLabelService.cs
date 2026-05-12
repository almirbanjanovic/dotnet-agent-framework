using Contoso.CrmApi.Services;

namespace Contoso.CrmApi.Tests;

// Test double for IReturnLabelService that records every call and lets
// tests force CreateAsync / VoidAsync to throw deterministically. Each
// test that wires this up is responsible for asserting on the recorded
// calls.
internal sealed class RecordingReturnLabelService : IReturnLabelService
{
    public List<(string TicketId, string OrderId)> CreateCalls { get; } = new();
    public List<string> VoidCalls { get; } = new();

    public Func<string, string, Exception?> CreateThrows { get; set; } = (_, _) => null;
    public Func<string, Exception?> VoidThrows { get; set; } = _ => null;

    public Task<ReturnLabel> CreateAsync(string ticketId, string orderId, CancellationToken ct)
    {
        CreateCalls.Add((ticketId, orderId));
        var ex = CreateThrows(ticketId, orderId);
        if (ex is not null)
        {
            throw ex;
        }
        var id = $"LBL-test{CreateCalls.Count:D4}";
        return Task.FromResult(new ReturnLabel(
            id, "UPS", $"https://example.com/return-labels/{id}.pdf", "2026-03-15"));
    }

    public Task VoidAsync(string labelId, CancellationToken ct)
    {
        VoidCalls.Add(labelId);
        var ex = VoidThrows(labelId);
        if (ex is not null)
        {
            throw ex;
        }
        return Task.CompletedTask;
    }
}
