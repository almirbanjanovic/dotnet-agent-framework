namespace Contoso.BffApi.Services;

internal static class FileNameValidator
{
    public static bool IsSafeFileName(string filename)
    {
        if (string.IsNullOrWhiteSpace(filename))
        {
            return false;
        }

        if (filename.Contains('/') || filename.Contains('\\'))
        {
            return false;
        }

        if (filename.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        return filename.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
    }
}
