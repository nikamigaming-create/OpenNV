using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutAddonNode(FalloutFormKey Form, uint Index, string Model, FalloutFormKey? Sound,
    ushort ParticleCap, ushort Flags);

// BSValueNode uses the source ADDN index, not a FormID or a model-name guess.
internal sealed class FalloutAddonNodes
{
    private static readonly ConditionalWeakTable<RuntimeLiveContentSource, FalloutAddonNodes> Catalogs = new();
    private readonly Dictionary<uint, FalloutAddonNode> _nodes = [];
    // The live runtime already owns the winning stack. Build this small index
    // from that owner before presentation instead of reopening every plugin at
    // the first muzzle flash. Standalone importers can still resolve on demand.
    internal static void Bind(RuntimeLiveContentSource content, FalloutPluginStack records) =>
        _ = Catalogs.GetValue(content, _ => new(records));
    internal static FalloutAddonNodes For(RuntimeLiveContentSource content) => Catalogs.GetValue(content, source =>
    {
        using var records = FalloutPluginStack.Load(source.PluginSources);
        return new(records);
    });

    internal FalloutAddonNodes(FalloutPluginStack records)
    {
        foreach (var record in records.EffectiveRecords("ADDN"))
        {
            var fields = record.ReadSubrecords().ToArray();
            var data = fields.Single(field => field.Signature == "DATA").Data.Span;
            var config = fields.Single(field => field.Signature == "DNAM").Data.Span;
            if (data.Length != 4 || config.Length != 4) throw new NotSupportedException("ADDN index/configuration extent is unbound.");
            var index = BinaryPrimitives.ReadUInt32LittleEndian(data);
            var path = FalloutPlugin.DecodeZeroTerminated(fields.Single(field => field.Signature == "MODL").Data.Span, "addon model").Replace('\\', '/');
            if (path.Length == 0 || path.StartsWith('/') || path.Contains(':') || path.Split('/').Any(part => part is "" or "." or ".."))
                throw new InvalidDataException("Addon model leaves the owned namespace.");
            if (!path.StartsWith("meshes/", StringComparison.OrdinalIgnoreCase)) path = "meshes/" + path;
            var sound = fields.SingleOrDefault(field => field.Signature == "SNAM").Data.Span;
            if (sound.Length is not (0 or 4)) throw new InvalidDataException("ADDN sound extent is invalid.");
            var definition = new FalloutAddonNode(record.FormKey, index, path, sound.IsEmpty ? null :
                record.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(sound)),
                BinaryPrimitives.ReadUInt16LittleEndian(config), BinaryPrimitives.ReadUInt16LittleEndian(config[2..]));
            if (!_nodes.TryAdd(index, definition)) throw new InvalidDataException($"Winning ADDN records repeat node index {index}.");
        }
    }

    internal FalloutAddonNode Get(uint index) => _nodes.TryGetValue(index, out var value) ? value :
        throw new NotSupportedException($"BSValueNode index {index} has no winning ADDN.");
}
