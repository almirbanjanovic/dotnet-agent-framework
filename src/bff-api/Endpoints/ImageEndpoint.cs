using Contoso.BffApi.Services;

namespace Contoso.BffApi.Endpoints;

// /api/v1/images/{filename} — proxies a product image from blob storage
// (or local disk in InMemory mode). Filename is validated by the
// IImageService implementation.

internal static class ImageEndpoint
{
    public static RouteHandlerBuilder MapImageEndpoint(this IEndpointRouteBuilder app)
    {
        return app.MapGet("/images/{filename}", HandleAsync);
    }

    private static async Task<IResult> HandleAsync(
        string filename,
        IImageService imageService,
        CancellationToken cancellationToken)
    {
        var image = await imageService.GetImageAsync(filename, cancellationToken);
        return image is null
            ? Results.NotFound()
            : Results.Stream(image.Value.content, image.Value.contentType);
    }
}
