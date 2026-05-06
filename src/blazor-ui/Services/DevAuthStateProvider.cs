using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace Contoso.BlazorUi.Services;

/// <summary>
/// Stub <see cref="AuthenticationStateProvider"/> for dev-auth mode (no
/// MSAL). Always reports the user as anonymous — dev-auth mode drives
/// the UI from <see cref="AuthStateProvider.SelectedCustomer"/>, not the
/// framework's <see cref="AuthenticationState"/>. Registering this stub
/// keeps <c>@inject AuthenticationStateProvider</c> resolvable and lets
/// <c>&lt;CascadingAuthenticationState&gt;</c> / <c>&lt;AuthorizeView&gt;</c>
/// render without exceptions.
/// </summary>
internal sealed class DevAuthStateProvider : AuthenticationStateProvider
{
    private static readonly AuthenticationState AnonymousState =
        new(new ClaimsPrincipal(new ClaimsIdentity()));

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
        => Task.FromResult(AnonymousState);
}
