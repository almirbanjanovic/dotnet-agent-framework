using Microsoft.Extensions.Configuration;

namespace Contoso.CrmApi.Tests;

internal static class TestDataHelper
{
    internal static string GetCrmDataPath()
    {
        var current = Directory.GetCurrentDirectory();
        while (!string.IsNullOrEmpty(current))
        {
            var candidate = Path.Combine(current, "data", "contoso-crm");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            current = Directory.GetParent(current)?.FullName ?? string.Empty;
        }

        throw new DirectoryNotFoundException("Could not locate data\\contoso-crm.");
    }

    internal static IConfiguration BuildConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CrmData:Path"] = GetCrmDataPath()
            })
            .Build();

    internal static string GetCsvPath(string fileName) =>
        Path.Combine(GetCrmDataPath(), fileName);

    internal static int CountCsvRows(string fileName) =>
        File.ReadLines(GetCsvPath(fileName))
            .Skip(1)
            .Count(line => !string.IsNullOrWhiteSpace(line));

    internal static string GetScratchFilePath(string fileName)
    {
        var scratch = Path.Combine(AppContext.BaseDirectory, "scratch");
        Directory.CreateDirectory(scratch);
        return Path.Combine(scratch, fileName);
    }
}
