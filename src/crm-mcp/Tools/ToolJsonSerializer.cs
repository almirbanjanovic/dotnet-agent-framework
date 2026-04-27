using System.Text.Json;
using System.Text.Json.Serialization;

namespace Contoso.CrmMcp.Tools;

internal static class ToolJsonSerializer
{
    private static readonly JsonSerializerOptions s_options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static string Serialize<T>(T value)
    {
        return JsonSerializer.Serialize(value, s_options);
    }
}
