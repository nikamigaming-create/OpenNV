using System.Buffers.Binary;
using System.Numerics;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutBodyPartGore(int DebrisCount, FalloutFormKey? Debris, FalloutFormKey? Explosion,
    float DebrisScale, FalloutFormKey? ImpactSet, byte Decals);

internal sealed record FalloutBodyPart(byte Type, string Name, string Node, string Target, byte Flags,
    float DamageMultiplier, byte HealthPercent, sbyte ActorValue, byte HitChance, byte ExplosionChance,
    float TrackingDegrees, FalloutBodyPartGore Sever, FalloutBodyPartGore Explode,
    Vector3 GoreTranslation, Vector3 GoreRotation, string? ReplacementModel, float ReplacementScale, string GoreBone);

// One source table supplies anatomical hit selection, damage and original gore
// resources. NAM1/NAM4 follow BPND and belong to that same part.
internal sealed record FalloutBodyPartData(FalloutFormKey Form, IReadOnlyList<FalloutBodyPart> Parts)
{
    internal static FalloutBodyPartData Read(FalloutPluginRecord record)
    {
        if (record.Signature != "BPTD") throw new InvalidDataException("Body-part source is not BPTD.");
        var parts = new List<FalloutBodyPart>();
        foreach (var fields in Groups(record.ReadSubrecords()))
        {
            var declarations = fields.Where(field => field.Signature == "BPND").ToArray();
            if (declarations.Length != 1 || declarations[0].Data.Length != 84)
                throw new NotSupportedException("Body-part declaration requires the 84-byte BPND layout.");
            var data = declarations[0].Data;
            float Number(int offset)
            {
                var value = BinaryPrimitives.ReadSingleLittleEndian(data.Span[offset..]);
                return float.IsFinite(value) ? value : throw new InvalidDataException("Body-part number is not finite.");
            }
            FalloutFormKey? Form(int offset) => record.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(data.Span[offset..]));
            string Text(string name)
            {
                var found = fields.Where(field => field.Signature == name).ToArray();
                if (found.Length > 1) throw new InvalidDataException("Body part repeats " + name);
                return found.Length == 0 ? "" : FalloutDialogueTopic.Text(found[0].Data.Span);
            }
            var sever = new FalloutBodyPartGore(BinaryPrimitives.ReadInt32LittleEndian(data.Span[28..]), Form(32), Form(36), Number(40), Form(68), data.Span[76]);
            var explode = new FalloutBodyPartGore(BinaryPrimitives.ReadUInt16LittleEndian(data.Span[10..]), Form(12), Form(16), Number(24), Form(72), data.Span[77]);
            var part = new FalloutBodyPart(data.Span[5], Text("BPTN"), Text("BPNN"), Text("BPNT"), data.Span[4],
                Number(0), data.Span[6], unchecked((sbyte)data.Span[7]), data.Span[8], data.Span[9], Number(20), sever, explode,
                new(Number(44), Number(48), Number(52)), new(Number(56), Number(60), Number(64)),
                FalloutNpcAppearanceResolver.PathField(record, "NAM1", "meshes", false, fields), Number(80), Text("NAM4"));
            if (part.Type > 14 || parts.Any(value => value.Type == part.Type) || (part.Flags & ~0x7f) != 0 ||
                part.DamageMultiplier < 0 || part.HealthPercent > 100 || part.ExplosionChance > 100 ||
                part.TrackingDegrees is < 0 or > 180 || part.ReplacementScale < 0 ||
                sever.DebrisCount < 0 || sever.DebrisScale < 0 || explode.DebrisScale < 0)
                throw new InvalidDataException("Body-part fields are invalid or repeated.");
            parts.Add(part);
        }
        if (parts.Count == 0) throw new InvalidDataException("Body-part source has no parts.");
        return new(record.FormKey, parts.OrderBy(part => part.Type).ToArray());
    }

    internal static IEnumerable<IReadOnlyList<FalloutPluginSubrecord>> Groups(IEnumerable<FalloutPluginSubrecord> fields)
    {
        var current = new List<FalloutPluginSubrecord>();
        var hasData = false;
        foreach (var field in fields)
        {
            if (field.Signature is not ("BPTN" or "BPNN" or "BPNT" or "BPNI" or "BPND" or "NAM1" or "NAM4" or "NAM5")) continue;
            if (hasData && field.Signature is "BPTN" or "BPNN")
            {
                yield return current; current = []; hasData = false;
            }
            current.Add(field);
            hasData |= field.Signature == "BPND";
        }
        if (current.Count != 0) yield return current;
    }
}
