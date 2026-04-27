namespace Contoso.BffApi.Services;

public interface IImageService
{
    Task<(Stream content, string contentType)?> GetImageAsync(string filename, CancellationToken ct = default);
}
