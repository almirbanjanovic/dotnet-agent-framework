using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Contoso.CrmApi.Tests;

public sealed class CrmApiWebApplicationFactory : WebApplicationFactory<Program>
{
    // Tests that want to assert on the outbound fraud-workflow call set
    // this BEFORE creating their HttpClient. Default = always-Accepted
    // stub so the existing test suite never opens a real socket to
    // localhost:5010.
    public StubHttpMessageHandler FraudWorkflowHandler { get; set; } =
        new(_ => StubHttpMessageHandler.Accepted("{\"alertId\":\"alert-test\"}"));

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
