using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Contoso.BffApi.Services;

namespace Contoso.BffApi.Tests;

public sealed class BffApiWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _orchestratorResponder;
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _crmResponder;

    public BffApiWebApplicationFactory(
        Func<HttpRequestMessage, HttpResponseMessage>? orchestratorResponder = null,
        Func<HttpRequestMessage, HttpResponseMessage>? crmResponder = null)
    {
        Environment.SetEnvironmentVariable("DataMode", "InMemory");
        _orchestratorResponder = orchestratorResponder ?? (_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
        _crmResponder = crmResponder ?? (_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataMode"] = "InMemory",
                ["BlazorUi:Origin"] = "http://localhost:5008",
                ["CrmApi:BaseUrl"] = "http://localhost",
                ["Orchestrator:BaseUrl"] = "http://localhost"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.AddHttpClient<OrchestratorClient>()
                .ConfigurePrimaryHttpMessageHandler(() => new StubHttpMessageHandler(_orchestratorResponder));

            services.AddHttpClient<CrmApiClient>()
                .ConfigurePrimaryHttpMessageHandler(() => new StubHttpMessageHandler(_crmResponder));
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Environment.SetEnvironmentVariable("DataMode", null);
        }

        base.Dispose(disposing);
    }
}
