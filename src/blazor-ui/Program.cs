using Contoso.BlazorUi;
using Contoso.BlazorUi.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddMudServices();
builder.Services.AddScoped<AuthStateProvider>();
builder.Services.AddScoped<CartService>();
builder.Services.AddScoped<ChatPanelState>();

var bffBaseUrl = BlazorUiConfiguration.GetBffBaseUrl(builder.Configuration);
var useDevAuth = BlazorUiConfiguration.IsDevAuthEnabled(builder.Configuration, builder.HostEnvironment);

if (useDevAuth)
{
    // Local / dev: no MSAL, customer dropdown selects identity. BffApiClient
    // sends X-Customer-Id; BFF reads that in InMemory mode.
    builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(bffBaseUrl) });
}
else
{
    // Production: MSAL PKCE — Blazor obtains a Bearer token for the BFF
    // scope and BaseAddressAuthorizationMessageHandler attaches it on every
    // outbound request. BFF validates the JWT and extracts the oid claim.
    builder.Services.AddMsalAuthentication(options =>
    {
        var clientId = builder.Configuration["AzureAd:BffClientId"]
            ?? throw new InvalidOperationException("AzureAd:BffClientId is required when not in dev auth mode.");
        var tenantId = builder.Configuration["AzureAd:TenantId"]
            ?? throw new InvalidOperationException("AzureAd:TenantId is required when not in dev auth mode.");

        options.ProviderOptions.Authentication.Authority = $"https://login.microsoftonline.com/{tenantId}";
        options.ProviderOptions.Authentication.ClientId = clientId;
        options.ProviderOptions.Authentication.ValidateAuthority = true;
        options.ProviderOptions.LoginMode = "redirect";

        // Request access tokens for the BFF API. The BFF exposes a default
        // scope matching its app registration's Application ID URI.
        options.ProviderOptions.DefaultAccessTokenScopes.Add($"api://{clientId}/access_as_user");

        // App roles flow as `roles` claim in the access token.
        options.UserOptions.RoleClaim = "roles";
    });

    builder.Services.AddScoped<BffAuthorizationMessageHandler>();

    builder.Services.AddHttpClient("Bff", client => client.BaseAddress = new Uri(bffBaseUrl))
        .AddHttpMessageHandler<BffAuthorizationMessageHandler>();

    builder.Services.AddScoped(sp =>
        sp.GetRequiredService<IHttpClientFactory>().CreateClient("Bff"));
}

builder.Services.AddScoped<BffApiClient>();

await builder.Build().RunAsync();
