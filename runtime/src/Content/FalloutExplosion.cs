using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

/// <summary>A winning Fallout explosion declaration; distances and effects retain source units.</summary>
internal sealed record FalloutExplosion(FalloutFormKey Form, float Force, float Damage, float Radius,
    FalloutFormKey? Light, FalloutFormKey? FirstSound, uint Flags, float ImageSpaceRadius,
    FalloutFormKey? ImpactDataSet, FalloutFormKey? SecondSound, float RadiationLevel,
    float RadiationDissipationSeconds, float RadiationRadius, uint SoundLevel,
    FalloutFormKey? PlacedImpactObject, FalloutFormKey? ObjectEffect, FalloutFormKey? ImageSpace,
    string? Model)
{
    // GECK flag IDs 1..6 are stored as bits 0..5 in EXPL DATA.
    private const uint AlwaysWorldOrientation = 1;
    private const uint KnockDownAlways = 1 << 1;
    private const uint KnockDownByFormula = 1 << 2;
    private const uint IgnoreLineOfSight = 1 << 3;
    private const uint PushSourceReferenceOnly = 1 << 4;
    private const uint IgnoreImageSpaceSwap = 1 << 5;
    private const uint KnownFlags = AlwaysWorldOrientation | KnockDownAlways | KnockDownByFormula |
        IgnoreLineOfSight | PushSourceReferenceOnly | IgnoreImageSpaceSwap;

    internal bool IgnoresLineOfSight => (Flags & IgnoreLineOfSight) != 0;
    internal bool UsesWorldOrientation => (Flags & AlwaysWorldOrientation) != 0;
    internal bool KnocksDownAlways => (Flags & KnockDownAlways) != 0;
    internal bool KnocksDownByFormula => (Flags & KnockDownByFormula) != 0;
    internal bool IgnoresImageSpaceSwap => (Flags & IgnoreImageSpaceSwap) != 0;
    internal bool HasUnpresentedVisuals => Light is not null || FirstSound is not null || SecondSound is not null ||
        ImageSpace is not null || ImageSpaceRadius > 0 || Model is not null || ImpactDataSet is not null;

    internal static FalloutExplosion Read(FalloutPluginStack records, FalloutFormKey key)
    {
        var record = records.GetEffective(key);
        if (record.Signature != "EXPL") throw new InvalidDataException("Explosion reference is not EXPL.");
        var fields = record.ReadSubrecords().ToArray();
        var data = fields.Single(field => field.Signature == "DATA").Data.ToArray();
        if (data.Length != 52) throw new NotSupportedException($"EXPL DATA extent {data.Length} is unbound.");
        float Number(int offset)
        {
            var value = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset));
            return float.IsFinite(value) ? value : throw new InvalidDataException("Explosion DATA contains a nonfinite value.");
        }
        FalloutFormKey? DataForm(int offset) => record.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset)));
        FalloutFormKey? FieldForm(string signature)
        {
            var subrecord = fields.SingleOrDefault(field => field.Signature == signature).Data;
            if (subrecord.IsEmpty) return null;
            if (subrecord.Length != 4) throw new InvalidDataException($"EXPL {signature} has an invalid extent.");
            return record.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(subrecord.Span));
        }

        var flags = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(20));
        var model = fields.SingleOrDefault(field => field.Signature == "MODL").Data;
        string? modelPath = null;
        if (!model.IsEmpty)
        {
            modelPath = FalloutPlugin.DecodeZeroTerminated(model.Span, "explosion model").Replace('\\', '/');
            if (modelPath.Split('/').Any(part => part is ".." or ".") || modelPath.Contains(':') || modelPath.StartsWith('/'))
                throw new InvalidDataException("Explosion model leaves the owned namespace.");
            if (!modelPath.StartsWith("meshes/", StringComparison.OrdinalIgnoreCase)) modelPath = "meshes/" + modelPath;
        }

        var result = new FalloutExplosion(key, Number(0), Number(4), Number(8), DataForm(12), DataForm(16), flags,
            Number(24), DataForm(28), DataForm(32), Number(36), Number(40), Number(44),
            BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(48)), FieldForm("INAM"), FieldForm("EITM"),
            FieldForm("MNAM"), modelPath);
        Check(result.Light, "LIGH");
        Check(result.FirstSound, "SOUN");
        Check(result.ImpactDataSet, "IPDS");
        Check(result.SecondSound, "SOUN");
        Check(result.ImageSpace, "IMAD");
        Check(result.ObjectEffect, "ENCH", "SPEL");
        Check(result.PlacedImpactObject, "TREE", "SOUN", "ACTI", "DOOR", "STAT", "FURN", "CONT", "ARMO", "AMMO", "LVLN", "LVLC", "MISC", "WEAP", "BOOK", "KEYM", "ALCH", "LIGH", "GRAS", "ASPC", "IDLM", "ARMA", "MSTT", "NOTE", "PWAT", "SCOL", "TACT", "TERM", "TXST", "CHIP", "CMNY", "CCRD", "IMOD");
        if (result.Force < 0 || result.Damage < 0 || result.Radius < 0 || result.ImageSpaceRadius < 0 ||
            result.RadiationLevel < 0 || result.RadiationDissipationSeconds < 0 || result.RadiationRadius < 0)
            throw new InvalidDataException("Explosion DATA contains a negative magnitude.");
        return result;

        void Check(FalloutFormKey? form, params string[] signatures)
        {
            if (form is { } value && !signatures.Contains(records.GetEffective(value).Signature, StringComparer.Ordinal))
                throw new InvalidDataException($"EXPL {key} reference {value} has the wrong record type.");
        }
    }

    // Health damage may run only when other source-owned explosion effects do
    // not need owners that are absent from the shared runtime yet.
    internal void RequireRuntimeDamageOwner()
    {
        if ((Flags & ~KnownFlags) != 0)
            throw new NotSupportedException($"Explosion {Form} flags 0x{Flags:x8} need their source effect owner.");
        if ((Flags & (KnockDownAlways | KnockDownByFormula)) != 0)
            throw new NotSupportedException($"Explosion {Form} needs its source knockdown owner.");
        if ((Flags & PushSourceReferenceOnly) != 0)
            throw new NotSupportedException($"Explosion {Form} pushes only its source reference.");
        if (Force != 0 || RadiationLevel != 0 || RadiationDissipationSeconds != 0 || RadiationRadius != 0 ||
            PlacedImpactObject is not null || ObjectEffect is not null)
            throw new NotSupportedException($"Explosion {Form} needs its force, radiation, placed-object or object-effect owner.");
    }
}
