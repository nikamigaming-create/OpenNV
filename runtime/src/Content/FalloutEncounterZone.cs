using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutEncounterZone(FalloutFormKey Form, string SourceSha256,
    FalloutFormKey? Owner, sbyte Rank, byte MinimumLevel, byte Flags)
{
    internal static FalloutEncounterZone Read(FalloutPluginRecord record)
    {
        if (record.Signature != "ECZN") throw new InvalidDataException("Encounter zone is not an ECZN record.");
        var fields = record.ReadSubrecords().Where(field => field.Signature == "DATA").ToArray();
        if (fields.Length != 1 || fields[0].Data.Length != 8)
            throw new InvalidDataException($"Encounter zone {record.FormKey} has invalid DATA extent.");
        var data = fields[0].Data.Span;
        if (data[5] > sbyte.MaxValue || (data[6] & ~3) != 0)
            throw new NotSupportedException($"Encounter zone {record.FormKey} has unbound level/flags.");
        return new(record.FormKey, Convert.ToHexString(SHA256.HashData(record.ReadData())).ToLowerInvariant(),
            record.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(data)),
            unchecked((sbyte)data[4]), data[5], data[6]);
    }

    internal int InitialLevel(int playerLevel, float scaling)
    {
        if (playerLevel is < 1 or > ushort.MaxValue || !float.IsFinite(scaling) || scaling <= 0)
            throw new InvalidDataException("Encounter initialization has an invalid player level or multiplier.");
        if (MinimumLevel == 0 || playerLevel < MinimumLevel && (Flags & 2) != 0) return playerLevel;
        if (playerLevel <= MinimumLevel) return MinimumLevel;
        var scaled = playerLevel * (double)scaling;
        if (scaled > ushort.MaxValue) throw new NotSupportedException("Encounter level exceeds its supported range.");
        return Math.Max(MinimumLevel, Math.Max(1, (int)scaled));
    }

    internal static FalloutFormKey? Assignment(FalloutPluginRecord record)
    {
        var fields = record.ReadSubrecords().Where(field => field.Signature == "XEZN").ToArray();
        if (fields.Length == 0) return null;
        if (fields.Length != 1 || fields[0].Data.Length != 4)
            throw new InvalidDataException($"{record.FormKey} has an invalid encounter-zone assignment.");
        return record.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(fields[0].Data.Span));
    }
}

internal sealed record FalloutEncounterZoneSnapshot(FalloutFormKey Zone, string SourceSha256,
    int FirstPlayerLevel, float ScalingMultiplier, int Level);
