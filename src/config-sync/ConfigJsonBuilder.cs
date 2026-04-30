using System.Text.Json.Nodes;

namespace ConfigSync;

/// <summary>
/// Helpers for translating Key Vault secret names (PascalCase--Hierarchy) into
/// nested <see cref="JsonObject"/> appsettings structures.
/// </summary>
internal static class ConfigJsonBuilder
{
    /// <summary>
    /// Sets a nested value in <paramref name="root"/> using <c>:</c>-delimited
    /// path segments, creating intermediate <see cref="JsonObject"/> nodes as
    /// needed. Mirrors the behaviour of .NET configuration's section binding.
    /// </summary>
    public static void SetNestedValue(JsonObject root, string configKey, string value)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(configKey);

        var segments = configKey.Split(':');
        var current = root;

        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (current[segments[i]] is not JsonObject child)
            {
                child = new JsonObject();
                current[segments[i]] = child;
            }

            current = child;
        }

        current[segments[^1]] = value;
    }
}
