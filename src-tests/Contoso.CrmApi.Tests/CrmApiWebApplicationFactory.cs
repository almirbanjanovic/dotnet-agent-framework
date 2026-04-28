using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Contoso.CrmApi.Tests;

public sealed class CrmApiWebApplicationFactory : WebApplicationFactory<Program>
{
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
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Environment.SetEnvironmentVariable("DataMode", null);
            Environment.SetEnvironmentVariable("CrmData__Path", null);
        }

        base.Dispose(disposing);
    }
}
