namespace Contoso.BffApi.Tests;

internal static class BffTestDataHelper
{
    internal static string GetRepositoryRoot()
    {
        var current = Directory.GetCurrentDirectory();
        while (!string.IsNullOrEmpty(current))
        {
            var candidate = Path.Combine(current, "data", "contoso-images");
            if (Directory.Exists(candidate))
            {
                return current;
            }

            current = Directory.GetParent(current)?.FullName ?? string.Empty;
        }

        throw new DirectoryNotFoundException("Could not locate data\\contoso-images.");
    }

    internal static string GetBffContentRoot() =>
        Path.Combine(GetRepositoryRoot(), "src", "bff-api");
}
