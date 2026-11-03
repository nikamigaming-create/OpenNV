using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenNV.Runtime.Content;

// Runtime references have no declaring plugin. In particular their source
// PROJ/EXPL base is not a substitute reference identity.
[JsonConverter(typeof(FalloutSpatialReferenceIdJsonConverter))]
internal readonly record struct FalloutSpatialReferenceId(FalloutFormKey? Source, uint? RuntimeId)
{
    internal FalloutSpatialReferenceId(string ownerPlugin, uint objectId) : this(new FalloutFormKey(ownerPlugin, objectId), null) { }
    internal static FalloutSpatialReferenceId Owned(FalloutFormKey source) => new(source, null);
    internal static FalloutSpatialReferenceId Runtime(uint id) => new((FalloutFormKey?)null, id);
    public static implicit operator FalloutSpatialReferenceId(FalloutFormKey source) => Owned(source);

    internal void Validate()
    {
        if ((Source is null) == (RuntimeId is null) ||
            Source is { } source && (string.IsNullOrWhiteSpace(source.OwnerPlugin) || source.ObjectId is 0 or > FalloutFormKey.ObjectIdMask) ||
            RuntimeId is { } id && ((id & 0xff000000) != 0xff000000 || (id & FalloutFormKey.ObjectIdMask) == 0))
            throw new InvalidDataException("Reference identity is neither one valid source reference nor one reserved runtime reference.");
    }

    internal FalloutFormKey RequireSource() => Source ??
        throw new NotSupportedException("Runtime reference has no declaring-plugin FormKey.");
    public override string ToString() => Source?.ToString() ?? $"runtime:{RuntimeId:x8}";
}

// Keep the v71 source-reference JSON shape. Only runtime references use the
// additional RuntimeId shape, so legacy actor/audio state needs no rewrite.
internal sealed class FalloutSpatialReferenceIdJsonConverter : JsonConverter<FalloutSpatialReferenceId>
{
    public FalloutSpatialReferenceIdJsonConverter() { }
    public override FalloutSpatialReferenceId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var value = JsonDocument.ParseValue(ref reader);
        var root = value.RootElement;
        FalloutSpatialReferenceId result;
        var runtimeName = options.PropertyNamingPolicy?.ConvertName("RuntimeId") ?? "RuntimeId";
        if (root.TryGetProperty(runtimeName, out var runtime))
        {
            if (root.EnumerateObject().Count() != 1) throw new InvalidDataException("Runtime identity contains source fields.");
            result = FalloutSpatialReferenceId.Runtime(runtime.GetUInt32());
        }
        else
        {
            result = FalloutSpatialReferenceId.Owned(JsonSerializer.Deserialize<FalloutFormKey>(root.GetRawText(), options));
        }
        if (result.RuntimeId is not null) result.Validate();
        return result;
    }

    public override void Write(Utf8JsonWriter writer, FalloutSpatialReferenceId value, JsonSerializerOptions options)
    {
        if (value.Source is { } source && value.RuntimeId is null)
        {
            JsonSerializer.Serialize(writer, source, options);
            return;
        }
        value.Validate(); writer.WriteStartObject();
        writer.WriteNumber(options.PropertyNamingPolicy?.ConvertName("RuntimeId") ?? "RuntimeId", value.RuntimeId!.Value);
        writer.WriteEndObject();
    }
}
