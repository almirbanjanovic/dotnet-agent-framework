using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;

namespace Contoso.BffApi.Services;

public sealed class BlobImageService : IImageService
{
    private readonly BlobContainerClient _containerClient;

    public BlobImageService(BlobServiceClient serviceClient, IConfiguration configuration)
    {
        var containerName = configuration["Storage:ImagesContainer"]
            ?? throw new InvalidOperationException("Storage:ImagesContainer configuration is required.");
        _containerClient = serviceClient.GetBlobContainerClient(containerName);
    }

    public async Task<(Stream content, string contentType)?> GetImageAsync(string filename, CancellationToken ct = default)
    {
        if (!FileNameValidator.IsSafeFileName(filename))
        {
            return null;
        }

        var blobClient = _containerClient.GetBlobClient(filename);
        if (!await blobClient.ExistsAsync(ct))
        {
            return null;
        }

        var download = await blobClient.DownloadStreamingAsync(cancellationToken: ct);
        var contentType = download.Value.Details.ContentType ?? "application/octet-stream";
        return (download.Value.Content, contentType);
    }
}
