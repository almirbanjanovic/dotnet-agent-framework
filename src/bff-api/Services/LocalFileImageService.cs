using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Hosting;

namespace Contoso.BffApi.Services;

public sealed class LocalFileImageService : IImageService
{
    private readonly string _rootPath;
    private readonly FileExtensionContentTypeProvider _contentTypeProvider = new();

    public LocalFileImageService(IHostEnvironment environment)
    {
        _rootPath = Path.GetFullPath(Path.Combine(
            environment.ContentRootPath,
            "..",
            "..",
            "data",
            "contoso-images"));
    }

    public Task<(Stream content, string contentType)?> GetImageAsync(string filename, CancellationToken ct = default)
    {
        if (!FileNameValidator.IsSafeFileName(filename))
        {
            return Task.FromResult<(Stream content, string contentType)?>(null);
        }

        var path = Path.Combine(_rootPath, filename);
        if (!File.Exists(path))
        {
            return Task.FromResult<(Stream content, string contentType)?>(null);
        }

        var contentType = _contentTypeProvider.TryGetContentType(filename, out var resolvedType)
            ? resolvedType
            : "application/octet-stream";
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult<(Stream content, string contentType)?>(new(stream, contentType));
    }
}
