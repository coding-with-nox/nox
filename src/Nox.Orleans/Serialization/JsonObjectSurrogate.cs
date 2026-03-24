using System.Text.Json.Nodes;
using Orleans;

namespace Nox.Orleans.Serialization;

[GenerateSerializer]
public struct JsonObjectSurrogate
{
    [Id(0)] public string Json;
}

[RegisterConverter]
public sealed class JsonObjectConverter : IConverter<JsonObject, JsonObjectSurrogate>
{
    public JsonObject ConvertFromSurrogate(in JsonObjectSurrogate surrogate) =>
        (JsonNode.Parse(string.IsNullOrEmpty(surrogate.Json) ? "{}" : surrogate.Json) as JsonObject)
        ?? new JsonObject();

    public JsonObjectSurrogate ConvertToSurrogate(in JsonObject value) =>
        new() { Json = value?.ToJsonString() ?? "{}" };
}
