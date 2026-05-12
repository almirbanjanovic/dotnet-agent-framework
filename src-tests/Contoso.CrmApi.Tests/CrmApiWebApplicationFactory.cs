using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Contoso.CrmApi.Tests;

public sealed class CrmApiWebApplicationFactory : WebApplicationFactory<Program>
{
    // Tests that want to assert on the outbound fraud-workflow call set
    // this BEFORE creating their HttpClient. Default = always-Accepted
    // stub so the existing test suite never opens a real socket to
    // localhost:5010.
    public StubHttpMessageHandler FraudWorkflowHandler { get; set; } =
        new(_ => StubHttpMessageHandler.Accepted("{\"alertId\":\"alert-test\"}"));

    // Override for the system clock seam used by the 30-day return-
    // window gate. Defaults to a FIXED date inside the seed-data return
    // window (2026-03-15) so all existing tests \u2014 which create return
    // tickets against orders delivered Feb-Mar 2026 \u2014 stay deterministic
    // regardless of the wall clock on the dev machine or CI runner.
    // Window-boundary tests pin their own provider to assert the
    // 29/30/31 day edges.
    public TimeProvider TimeProvider { get; set; } =
        new FixedTimeProvider(new DateTimeOffset(2026, 3, 15, 12, 0, 0, TimeSpan.Zero));

    // Override for the prepaid return-label issuer. Tests that want to
    // simulate carrier failures swap in a throwing impl before the
    // first CreateClient call.
    public Contoso.CrmApi.Services.IReturnLabelService? ReturnLabelService { get; set; }

    public CrmApiWebApplicationFactory()
    {
        Environment.SetEnvironmentVariable("DataMode", "InMemory");
        Environment.SetEnvironmentVariable("CrmData__Path", TestDataHelper.GetCrmDataPath());
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataMode"] = "InMemory",
                ["CrmData:Path"] = TestDataHelper.GetCrmDataPath()
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // Replace the FraudWorkflowClient's primary handler so the
            // fire-and-forget refund alert never opens a real socket.
            services.AddHttpClient<Contoso.CrmApi.Services.FraudWorkflowClient>(c =>
            {
                c.BaseAddress = new Uri("http://fraud-workflow.test");
                c.Timeout = TimeSpan.FromSeconds(5);
            })
            .ConfigurePrimaryHttpMessageHandler(() => FraudWorkflowHandler);

            // Swap the system clock if a test pinned a custom provider.
            services.AddSingleton(TimeProvider);

            // Swap the return-label issuer if a test pinned one. Replace
            // every prior registration so the throwing fake fully
            // overrides Program.cs's FakeReturnLabelService.
            if (ReturnLabelService is not null)
            {
                services.RemoveAll<Contoso.CrmApi.Services.IReturnLabelService>();
                services.AddSingleton(ReturnLabelService);
            }
        });
    }

    protected override void Dispose(bool disposing)
    {
        // We deliberately do NOT clear DataMode/CrmData__Path here:
        // SupportTicketCancelTests creates per-test factories with
        // `await using var factory = ...`. Disposing the per-test
        // factory used to null these env vars, which then nuked any
        // class-fixture factory that hadn't yet started its host
        // (host startup is lazy until the first CreateClient call).
        // Leaving the env vars in place is safe: every constructor
        // re-sets them to the same in-memory test values.
        base.Dispose(disposing);
    }
}
