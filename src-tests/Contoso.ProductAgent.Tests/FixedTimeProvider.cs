namespace Contoso.ProductAgent.Tests;

// Tiny deterministic TimeProvider for tests. Returns a frozen "now"
// regardless of when the test is invoked. Mirrors the same helper in
// Contoso.CrmApi.Tests — duplicated here per the component-independence
// rule (each test project is its own component).
internal sealed class FixedTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _now;

    public FixedTimeProvider(DateTimeOffset now)
    {
        _now = now;
    }

    public override DateTimeOffset GetUtcNow() => _now;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}
