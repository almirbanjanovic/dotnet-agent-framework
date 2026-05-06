using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Configuration;

namespace Contoso.BlazorUi.Services;

/// <summary>
/// Holds the "who am I shopping as" state for the Blazor UI.
///
/// Two modes:
///   - <b>Dev auth</b> (Local Track without MSAL, automated tests): the
///     impersonation dropdown picks one of 8 seeded customers; nothing
///     is asked of the BFF.
///   - <b>Real Entra ID</b> (default Local Track + Full Track): no
///     dropdown — the signed-in user IS the customer. The shell calls
///     <see cref="LoadSignedInCustomerAsync"/> after login; the BFF
///     resolves the JWT to a CRM customer and returns it from
///     <c>GET /api/v1/me</c>.
/// </summary>
public sealed class AuthStateProvider
{
    private readonly List<CustomerOption> _customers =
    [
        new CustomerOption("101", "Emma Wilson"),
        new CustomerOption("102", "James Chen"),
        new CustomerOption("103", "Sarah Miller"),
        new CustomerOption("104", "David Park"),
        new CustomerOption("105", "Lisa Torres"),
        new CustomerOption("106", "Mike Johnson"),
        new CustomerOption("107", "Anna Roberts"),
        new CustomerOption("108", "Tom Garcia")
    ];

    public AuthStateProvider(IConfiguration configuration, IWebAssemblyHostEnvironment environment)
    {
        UseDevAuth = BlazorUiConfiguration.IsDevAuthEnabled(configuration, environment);
        Customers = _customers;

        if (UseDevAuth)
        {
            SelectedCustomer = _customers.FirstOrDefault();
        }
    }

    public event Action? CustomerChanged;

    public bool UseDevAuth { get; }

    public IReadOnlyList<CustomerOption> Customers { get; }

    public CustomerOption? SelectedCustomer { get; private set; }

    public string? CustomerId => SelectedCustomer?.Id;

    public string? Email { get; private set; }

    /// <summary>
    /// When <see cref="LoadSignedInCustomerAsync"/> fails or the BFF returns
    /// no customer mapping, this captures a short message that the UI can
    /// surface to the user (e.g. on the Profile page) instead of silently
    /// showing an empty state.
    /// </summary>
    public string? LastLoadError { get; private set; }

    /// <summary>
    /// Becomes <c>true</c> after the first <see cref="LoadSignedInCustomerAsync"/>
    /// call completes (success or failure). Lets pages distinguish "still
    /// loading" from "tried and got nothing".
    /// </summary>
    public bool HasAttemptedLoad { get; private set; }

    public void SetCustomer(CustomerOption? customer)
    {
        if (customer is null || SelectedCustomer?.Id == customer.Id)
        {
            return;
        }

        SelectedCustomer = customer;
        CustomerChanged?.Invoke();
    }

    /// <summary>
    /// Asks the BFF "who am I" and seeds <see cref="SelectedCustomer"/> from
    /// the response. Called once after MSAL completes the sign-in flow. Has
    /// no effect when <see cref="UseDevAuth"/> is true.
    /// </summary>
    public async Task LoadSignedInCustomerAsync(BffApiClient client, CancellationToken ct = default)
    {
        if (UseDevAuth || SelectedCustomer is not null)
        {
            return;
        }

        try
        {
            var me = await client.GetMeAsync(ct);
            if (string.IsNullOrWhiteSpace(me.CustomerId))
            {
                LastLoadError = "Signed in successfully, but the BFF could not map your account to a Contoso Outdoors customer record. "
                    + $"(BFF returned displayName='{me.DisplayName}', email='{me.Email}'.)";
                return;
            }

            // Prefer the friendly name from the seed list (matches the
            // capitalization used in product card greetings) when the
            // resolved customer ID matches one of the 8 seeded customers.
            // Falls back to whatever the BFF returned.
            var seeded = _customers.FirstOrDefault(c =>
                string.Equals(c.Id, me.CustomerId, StringComparison.OrdinalIgnoreCase));
            var displayName = !string.IsNullOrWhiteSpace(me.DisplayName)
                ? me.DisplayName!
                : seeded?.Name ?? $"Customer #{me.CustomerId}";

            SelectedCustomer = new CustomerOption(me.CustomerId, displayName);
            Email = me.Email;
            LastLoadError = null;
            CustomerChanged?.Invoke();
        }
        catch (Exception ex)
        {
            // Surface the failure on the Profile page so we stop debugging blind.
            LastLoadError = $"GET /api/v1/me failed: {ex.GetType().Name}: {ex.Message}";
        }
        finally
        {
            HasAttemptedLoad = true;
            CustomerChanged?.Invoke();
        }
    }
}

public sealed record CustomerOption(string Id, string Name);
