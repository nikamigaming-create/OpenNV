using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutActorAnimationObject(FalloutFormKey Form, string ModelPath);
internal sealed record FalloutActorIdleSource(FalloutFormKey Form, string AnimationPath,
    IReadOnlyList<FalloutActorAnimationObject> Objects)
{
    internal static FalloutActorIdleSource Resolve(FalloutPluginStack stack, string editorId)
        => Resolve(stack, FalloutDialogueTopic.Find(stack, "IDLE", editorId));

    internal static FalloutActorIdleSource Resolve(FalloutPluginStack stack, FalloutPluginRecord idle)
    {
        if (idle.Signature != "IDLE") throw new InvalidDataException("Selected animation record is not IDLE.");
        var objects = new List<FalloutActorAnimationObject>();
        foreach (var record in stack.EffectiveRecords("ANIO"))
        {
            var fields = record.ReadSubrecords().Where(field => field.Signature == "DATA").ToArray();
            if (fields.Length == 0) continue;
            if (fields.Length != 1) throw new InvalidDataException($"ANIO {record.FormKey} has duplicate IDLE identity.");
            var data = fields[0].Data;
            if (data.Length != 4) throw new InvalidDataException($"ANIO {record.FormKey} has incomplete IDLE identity.");
            // Reject non-candidates before resolving their namespace. Unrelated
            // dangling links are not prerequisites for this IDLE's attachment.
            if ((BinaryPrimitives.ReadUInt32LittleEndian(data.Span) & 0x00ffffff) != idle.FormKey.ObjectId) continue;
            // ANIO.DATA references its owning IDLE, with the declaring plugin's
            // master table applied before comparing the winning identities.
            if (FalloutDialogueTopic.RequiredForm(record, "DATA") != idle.FormKey) continue;
            objects.Add(new(record.FormKey, ModelPath(record)));
        }
        return new(idle.FormKey, ModelPath(idle), objects);
    }

    private static string ModelPath(FalloutPluginRecord record)
    {
        var path = FalloutDialogueTopic.Text(record.ReadSubrecords().Single(field => field.Signature == "MODL").Data.Span)
            .Replace('\\', '/');
        return path.StartsWith("meshes/", StringComparison.OrdinalIgnoreCase) ? path : "meshes/" + path;
    }
}
