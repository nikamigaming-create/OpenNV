using System.Text.Json;
using System.Text.Json.Serialization;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

// FormKey is a typed source identity, not a JSON property-name grammar. Keep
// ordered exact identities as rows while preserving the live map API.
internal sealed class FalloutCellProcessEpochsJson : JsonConverter<IReadOnlyDictionary<FalloutFormKey, long>>
{
    private sealed record Epoch(FalloutFormKey Cell, long Generation);
    public override IReadOnlyDictionary<FalloutFormKey, long> Read(ref Utf8JsonReader reader,
        Type typeToConvert, JsonSerializerOptions options)
    {
        var rows = JsonSerializer.Deserialize<Epoch[]>(ref reader, options) ??
            throw new JsonException("CELL epoch rows are absent.");
        var result = new Dictionary<FalloutFormKey, long>(FalloutFormKeyComparer.Instance);
        foreach (var row in rows)
            if (row is null || string.IsNullOrWhiteSpace(row.Cell.OwnerPlugin) || row.Cell.ObjectId is 0 or > FalloutFormKey.ObjectIdMask ||
                row.Generation < 1 || !result.TryAdd(row.Cell, row.Generation))
                throw new JsonException("CELL epoch rows repeat or lose an exact source identity/generation.");
        return result;
    }
    public override void Write(Utf8JsonWriter writer, IReadOnlyDictionary<FalloutFormKey, long> value,
        JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value.Select(pair => new Epoch(pair.Key, pair.Value)).ToArray(), options);
}
