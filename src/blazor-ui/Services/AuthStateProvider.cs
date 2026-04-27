using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Configuration;

namespace Contoso.BlazorUi.Services;

public sealed class AuthStateProvider
{
    private readonly List<CustomerOption> _customers =
    [
        new CustomerOption("101", "Emma Wilson"),
        new CustomerOption("102", "James Chen"),
        new CustomerOption("103", "Sarah Johnson"),
        new CustomerOption("104", "David Park"),
        new CustomerOption("105", "Lisa Torres"),
        new CustomerOption("106", "Mike Johnson"),
        new CustomerOption("107", "Anna Roberts"),
        new CustomerOption("108", "Tom Garcia")
    ];

    public AuthStateProvider(IConfiguration configuration, IWebAssemblyHostEnvironment environment)
    {
        UseDevAuth = !string.IsNullOrWhiteSpace(configuration["DataMode"]) || environment.IsDevelopment();
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

    public void SetCustomer(CustomerOption? customer)
    {
        if (customer is null || SelectedCustomer?.Id == customer.Id)
        {
            return;
        }

        SelectedCustomer = customer;
        CustomerChanged?.Invoke();
    }
}

public sealed record CustomerOption(string Id, string Name);
