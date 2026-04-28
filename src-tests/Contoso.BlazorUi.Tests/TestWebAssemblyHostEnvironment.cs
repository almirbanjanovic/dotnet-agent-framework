using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.FileProviders;

namespace Contoso.BlazorUi.Tests;

internal sealed class TestWebAssemblyHostEnvironment : IWebAssemblyHostEnvironment
{
    public string Environment { get; set; } = "Production";

    public string EnvironmentName { get; set; } = "Production";

    public string ApplicationName { get; set; } = "TestApp";

    public string ContentRootPath { get; set; } = "C:\\";

    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();

    public string BaseAddress { get; set; } = "http://localhost";
}
