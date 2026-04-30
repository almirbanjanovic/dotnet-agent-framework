using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Configuration;

namespace Contoso.BlazorUi.Services;

internal static class BlazorUiConfiguration
{
    internal const string DefaultBffBaseUrl = "http://localhost:5007";

    internal static string GetBffBaseUrl(IConfiguration configuration) =>
        configuration["Bff:BaseUrl"] ?? DefaultBffBaseUrl;

    internal static bool IsDevAuthEnabled(IConfiguration configuration, IWebAssemblyHostEnvironment environment)
    {
        // Explicit opt-in to real Microsoft Entra ID via MSAL: when
        // AzureAd:Enabled is true, dev auth is forced off — even on the
        // Local Track. This is what lets developers exercise the
        // production sign-in flow without provisioning AKS.
        if (string.Equals(configuration["AzureAd:Enabled"], "true", System.StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(configuration["DataMode"]) || environment.IsDevelopment();
    }
}
