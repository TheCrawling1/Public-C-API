using System.Text.Json;
using System.Text.Json.Serialization;

namespace ApiRouter.Serialization;

/// <summary>Shared serializer settings so stored templates round-trip with the API.</summary>
public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}
