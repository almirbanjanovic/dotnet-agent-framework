using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Configuration;

namespace Contoso.BlazorUi.Services;

internal static class BlazorUiConfiguration
{
    internal const string DefaultBffBaseUrl = "http://localhost:5007";

    internal static string GetBffBaseUrl(IConfiguration configuration) =>
        configuration["Bff:BaseUrl"] ?? DefaultBffBaseUrl;

    internal static bool IsDevAuthEnabled(IConfiguration configuration, IWebAssemblyHostEnvironment environment) =>
        !string.IsNullOrWhiteSpace(configuration["DataMode"]) || environment.IsDevelopment();
}
