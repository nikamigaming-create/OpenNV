using System.Buffers.Binary;
using System.Text;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutCreatureAppearance(FalloutFormKey Creature, FalloutFormKey? Reference,
    FalloutFormKey ModelOwner, FalloutFormKey StatsOwner, string SkeletonPath, float BaseScale,
    uint ModelFlags, IReadOnlyList<string> Models, IReadOnlyList<string> AnimationFiles);

// CREA model and stats template groups are independent. NIFZ contains the
// enabled body parts relative to MODL's directory, not replacement skeletons.
internal static class FalloutCreatureAppearanceResolver
{
    internal static FalloutCreatureAppearance Resolve(FalloutPluginStack stack, FalloutFormKey creature,
        FalloutFormKey? reference = null)
    {
        var record = stack.GetEffective(creature);
        if (record.Signature != "CREA") throw new InvalidDataException($"{creature} is not CREA.");
        if (reference is { } key)
        {
            var placed = stack.GetEffective(key);
            if (placed.Signature != "ACRE" || ReadForm(placed, "NAME") != creature)
                throw new InvalidDataException($"{reference} is not a reference to CREA {creature}.");
        }
        var model = TemplateOwner(stack, record, 64);
        var stats = TemplateOwner(stack, record, 2);
        var skeleton = ModelPath(ReadString(Field(model, "MODL").Span), "meshes");
        var directory = skeleton[..skeleton.LastIndexOf('/')];
        var models = ReadList(model, "NIFZ").Select(path => ModelPath(path, directory)).ToArray();
        if (models.Distinct(StringComparer.OrdinalIgnoreCase).Count() != models.Length)
            throw new InvalidDataException($"CREA {model.FormKey} repeats a body model.");
        var scale = BinaryPrimitives.ReadSingleLittleEndian(Field(stats, "BNAM", 4).Span);
        if (!float.IsFinite(scale) || scale <= 0)
            throw new InvalidDataException($"CREA {stats.FormKey} has an invalid base scale.");
        var animations = ReadList(model, "KFFZ").Select(path => ModelPath(path, directory, ".kf")).ToArray();
        return new(creature, reference, model.FormKey, stats.FormKey, skeleton, scale,
            BinaryPrimitives.ReadUInt32LittleEndian(Field(model, "ACBS", 24).Span), models, animations);
    }

    internal static FalloutPluginRecord TemplateOwner(FalloutPluginStack stack, FalloutPluginRecord record, ushort group)
        => FalloutActorTemplateOwner.Resolve(stack, record, group);

    internal static string SelectIdle(FalloutCreatureAppearance appearance, IEnumerable<string> resources)
    {
        var directory = appearance.SkeletonPath[..appearance.SkeletonPath.LastIndexOf('/')];
        var direct = directory + "/mtidle.kf";
        var locomotion = directory + "/locomotion/mtidle.kf";
        var candidates = appearance.AnimationFiles.Concat(resources.Select(path => path.Replace('\\', '/')))
            .Where(path => path.Equals(direct, StringComparison.OrdinalIgnoreCase) ||
                path.Equals(locomotion, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return candidates.Length == 1 ? candidates[0] : throw new NotSupportedException(
            $"CREA {appearance.Creature} has {candidates.Length} source movement-idle candidates; selection is unresolved.");
    }

    private static string ModelPath(string path, string directory, string extension = ".nif")
    {
        path = path.Replace('\\', '/');
        if (path.Length == 0 || path.StartsWith('/') || path.Contains(':') ||
            path.Split('/').Any(part => part is "" or "." or "..") || !path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("CREA resource path is invalid or leaves its owned namespace.");
        return path.StartsWith("meshes/", StringComparison.OrdinalIgnoreCase) ? path : directory + "/" + path;
    }

    private static string[] ReadList(FalloutPluginRecord record, string signature)
    {
        var fields = record.ReadSubrecords().Where(field => field.Signature == signature).ToArray();
        if (fields.Length == 0) return [];
        if (fields.Length != 1) throw new InvalidDataException($"CREA {record.FormKey} repeats {signature}.");
        var data = fields[0].Data.Span;
        if (data.Length == 0 || data[^1] != 0) throw new InvalidDataException($"CREA {signature} is not null terminated.");
        var text = Encoding.Latin1.GetString(data).TrimEnd('\0');
        return text.Length == 0 ? [] : text.Split('\0');
    }

    private static string ReadString(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 2 || bytes[^1] != 0 || bytes[..^1].Contains((byte)0))
            throw new InvalidDataException("CREA path is not one terminated source string.");
        return Encoding.Latin1.GetString(bytes[..^1]);
    }

    private static FalloutFormKey ReadForm(FalloutPluginRecord record, string signature)
        => record.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(Field(record, signature, 4).Span));

    private static ReadOnlyMemory<byte> Field(FalloutPluginRecord record, string signature, int? length = null)
    {
        var fields = record.ReadSubrecords().Where(field => field.Signature == signature).ToArray();
        if (fields.Length != 1 || length is { } count && fields[0].Data.Length != count)
            throw new InvalidDataException($"CREA {record.FormKey} has invalid {signature} data.");
        return fields[0].Data;
    }
}
