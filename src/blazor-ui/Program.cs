using Contoso.BlazorUi;
using Contoso.BlazorUi.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddMudServices();
builder.Services.AddScoped<AuthStateProvider>();

var bffBaseUrl = builder.Configuration["Bff:BaseUrl"] ?? "http://localhost:5007";
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(bffBaseUrl) });
builder.Services.AddScoped<BffApiClient>();

await builder.Build().RunAsync();
