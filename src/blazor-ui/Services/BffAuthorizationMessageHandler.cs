using Microsoft.AspNetCore.Components.WebAssembly.Authentication;

namespace Contoso.BlazorUi.Services;

/// <summary>
/// Adds the BFF API access token (acquired via MSAL) to outbound requests.
/// Inherits from <see cref="AuthorizationMessageHandler"/>; the framework
/// silently acquires/refreshes tokens before each request.
/// </summary>
public sealed class BffAuthorizationMessageHandler : AuthorizationMessageHandler
{
    public BffAuthorizationMessageHandler(
        IAccessTokenProvider provider,
        Microsoft.AspNetCore.Components.NavigationManager navigation,
        Microsoft.Extensions.Configuration.IConfiguration configuration)
        : base(provider, navigation)
    {
        var clientId = configuration["AzureAd:BffClientId"]
            ?? throw new InvalidOperationException("AzureAd:BffClientId is required.");
        var bffBaseUrl = BlazorUiConfiguration.GetBffBaseUrl(configuration);

        ConfigureHandler(
            authorizedUrls: [bffBaseUrl],
            scopes: [$"api://{clientId}/access_as_user"]);
    }
}
