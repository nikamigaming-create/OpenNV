using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutCombatStyle(FalloutFormKey Form, uint WeaponRestriction, float MinimumRangeMultiplier,
    float MaximumRangeMultiplier, float MinimumFiringDelayMultiplier, float MaximumFiringDelayMultiplier)
{
    internal static FalloutCombatStyle Read(FalloutPluginRecord record)
    {
        if (record.Signature != "CSTY") throw new InvalidDataException("Combat style is not CSTY.");
        var data = record.ReadSubrecords().Single(field => field.Signature == "CSSD").Data.Span;
        if (data.Length != 64) throw new NotSupportedException("Combat-style simple data extent is unbound.");
        var result = new FalloutCombatStyle(record.FormKey, BinaryPrimitives.ReadUInt32LittleEndian(data[40..]),
            BinaryPrimitives.ReadSingleLittleEndian(data[32..]), BinaryPrimitives.ReadSingleLittleEndian(data[44..]),
            BinaryPrimitives.ReadSingleLittleEndian(data[56..]), BinaryPrimitives.ReadSingleLittleEndian(data[60..]));
        if (result.WeaponRestriction > 2 || new[] { result.MinimumRangeMultiplier, result.MaximumRangeMultiplier,
                result.MinimumFiringDelayMultiplier, result.MaximumFiringDelayMultiplier }.Any(value => !float.IsFinite(value) || value < 0) ||
            result.MinimumRangeMultiplier > result.MaximumRangeMultiplier || result.MinimumFiringDelayMultiplier > result.MaximumFiringDelayMultiplier)
            throw new InvalidDataException("Combat-style range, delay or weapon restriction is invalid.");
        return result;
    }
}
