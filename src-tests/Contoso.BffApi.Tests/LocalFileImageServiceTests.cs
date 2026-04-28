using Contoso.BffApi.Services;
using FluentAssertions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Contoso.BffApi.Tests;

public class LocalFileImageServiceTests
{
    [Fact]
    public async Task GetImageAsync_ValidFilename_ReturnsStreamAndContentType()
    {
        var service = new LocalFileImageService(new TestHostEnvironment());

        var result = await service.GetImageAsync("alpine-summit-jacket.png");

        result.Should().NotBeNull();
        var (content, contentType) = result!.Value;
        await using var stream = content;
        contentType.Should().Be("image/png");
        stream.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetImageAsync_PathTraversal_ReturnsNull()
    {
        var service = new LocalFileImageService(new TestHostEnvironment());

        var result = await service.GetImageAsync("../../etc/passwd");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetImageAsync_BackslashTraversal_ReturnsNull()
    {
        var service = new LocalFileImageService(new TestHostEnvironment());

        var result = await service.GetImageAsync("..\\windows\\system32");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetImageAsync_NonexistentFile_ReturnsNull()
    {
        var service = new LocalFileImageService(new TestHostEnvironment());

        var result = await service.GetImageAsync("doesnotexist.png");

        result.Should().BeNull();
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Contoso.BffApi.Tests";
        public string ContentRootPath { get; set; } = BffTestDataHelper.GetBffContentRoot();
        public IFileProvider ContentRootFileProvider { get; set; } =
            new PhysicalFileProvider(BffTestDataHelper.GetBffContentRoot());
    }
}
