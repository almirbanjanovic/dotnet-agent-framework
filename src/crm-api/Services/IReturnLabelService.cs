namespace Contoso.CrmApi.Services;

// Issuer/voider for prepaid return shipping labels. The fake impl
// (FakeReturnLabelService) generates synthetic IDs and an example.com
// URL so the local-dev / lab demo stays fully offline. A real impl
// would call Shippo / EasyPost / a carrier API, owning the HTTP
// client and rate-limit/retry policy itself.
//
// SECURITY (real impl): the URL returned by CreateAsync is treated as
// opaque by the rest of the system and is NOT linkified by the UI.
// A real carrier label download endpoint must be authenticated; the
// stored URL should expire (signed/short-lived) so a leaked DTO does
// not bypass the customer-owner check on the support ticket.
//
// Concurrency contract: all impls MUST be thread-safe (registered as
// a singleton) and MUST NOT capture scoped DI services. VoidAsync MUST
// be idempotent at the carrier (calling it twice on the same labelId
// is a successful no-op on the second call) so the API endpoint can
// rely on best-effort retry semantics.
public interface IReturnLabelService
{
    Task<ReturnLabel> CreateAsync(string ticketId, string orderId, CancellationToken ct);

    Task VoidAsync(string labelId, CancellationToken ct);
}

public sealed record ReturnLabel(string Id, string Carrier, string Url, string CreatedAt);
