namespace Contoso.CrmApi.Tests;

// Tiny deterministic TimeProvider for tests. Returns a frozen "now"
// regardless of when the test is invoked. We do NOT advance time
// implicitly \u2014 if a test needs to simulate the clock progressing,
// it should construct a new factory with a different fixed instant.
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
