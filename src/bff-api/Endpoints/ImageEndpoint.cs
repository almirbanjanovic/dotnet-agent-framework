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
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var image = await imageService.GetImageAsync(filename, cancellationToken);
        if (image is null)
        {
            return Results.NotFound();
        }

        // Product images are immutable for the lifetime of a deploy —
        // they only change when the seed/upload tool runs against a fresh
        // catalog. Allow browsers and any intermediate CDN to cache for
        // an hour so a chat with several products doesn't refetch the
        // same blob on every render. Public + max-age is safe because
        // the endpoint is AllowAnonymous and the content is non-sensitive.
        httpContext.Response.Headers.CacheControl = "public,max-age=3600,immutable";
        return Results.Stream(image.Value.content, image.Value.contentType);
    }
}
