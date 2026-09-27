using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutDestructionStage(byte HealthPercent, byte Index, byte ModelStage, byte Flags,
    int SelfDamagePerSecond, FalloutFormKey? Explosion, FalloutFormKey? Debris, int DebrisCount, string? Model)
{
    internal bool CapDamage => (Flags & 1) != 0;
    internal bool Disable => (Flags & 2) != 0;
    internal bool Destroy => (Flags & 4) != 0;
}

internal sealed record FalloutDestructible(FalloutFormKey Form, string Sha256, int Health, byte Flags,
    IReadOnlyList<FalloutDestructionStage> Stages)
{
    internal static FalloutDestructible? Read(FalloutPluginStack records, FalloutFormKey key)
    {
        var record = records.GetEffective(key);
        var fields = record.ReadSubrecords().ToArray();
        var start = Array.FindIndex(fields, field => field.Signature == "DEST");
        if (start < 0) return null;
        var header = fields[start].Data.Span;
        if (header.Length != 8) throw new InvalidDataException("DEST header extent is invalid.");
        var health = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (health <= 0) throw new InvalidDataException("Destructible health must be positive.");
        var stages = new List<FalloutDestructionStage>();
        for (var i = start + 1; i < fields.Length && fields[i].Signature == "DSTD"; i++)
        {
            var data = fields[i].Data.Span;
            if (data.Length != 20) throw new InvalidDataException("DSTD extent is invalid.");
            var percent = data[0]; var index = data[1]; var modelStage = data[2]; var flags = data[3];
            var dps = BinaryPrimitives.ReadInt32LittleEndian(data[4..]);
            var explosion = record.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(data[8..]));
            var debris = record.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(data[12..]));
            var count = BinaryPrimitives.ReadInt32LittleEndian(data[16..]);
            if (percent > 100 || index != stages.Count || modelStage > 8 || (flags & ~7) != 0 || dps < 0 || count < 0 ||
                stages.Count != 0 && percent > stages[^1].HealthPercent)
                throw new NotSupportedException($"Destruction stage {key}/{index} is outside the source contract.");
            if (explosion is { } blast && records.GetEffective(blast).Signature != "EXPL" ||
                debris is { } fragment && records.GetEffective(fragment).Signature != "DEBR")
                throw new InvalidDataException("Destruction stage effect has the wrong record type.");
            string? model = null;
            while (++i < fields.Length && fields[i].Signature is "DMDL" or "DMDT")
                if (fields[i].Signature == "DMDL")
                {
                    if (model is not null) throw new InvalidDataException("Duplicate destruction replacement model.");
                    model = FalloutPlugin.DecodeZeroTerminated(fields[i].Data.Span, "destruction model").Replace('\\', '/');
                    if (model.Length == 0 || model.Contains(':') || model.StartsWith('/') || model.Split('/').Any(part => part is ".." or "."))
                        throw new InvalidDataException("Destruction model leaves the owned namespace.");
                    if (!model.StartsWith("meshes/", StringComparison.OrdinalIgnoreCase)) model = "meshes/" + model;
                }
            if (i >= fields.Length || fields[i].Signature != "DSTF" || fields[i].Data.Length != 0)
                throw new InvalidDataException("Destruction stage lacks its end marker.");
            stages.Add(new(percent, index, modelStage, flags, dps, explosion, debris, count, model));
        }
        if (stages.Count != header[4]) throw new InvalidDataException("Destruction stage count differs from DEST.");
        // Only bit zero has declared meaning. Retail writes nonzero padding
        // bits in this byte; preserve them without treating them as enable bits.
        return new(key, Convert.ToHexString(SHA256.HashData(record.ReadData())).ToLowerInvariant(), health, header[5], stages);
    }
}
