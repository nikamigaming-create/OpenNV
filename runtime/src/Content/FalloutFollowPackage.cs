using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutFollowPackage(FalloutFormKey Form, FalloutFormKey Target, int Distance, uint Flags,
    bool Accompany = false)
{
    internal bool DefensiveCombat => (Flags & 0x400000) != 0;
    internal bool Running => (Flags & 0x2000) != 0;
    internal bool AllowsUnprovokedCombat(FalloutFormKey? attackedTarget) =>
        !Accompany || !DefensiveCombat || attackedTarget == Target;
    internal static FalloutFollowPackage Read(FalloutPluginRecord record) => Read(record, false);
    internal static FalloutFollowPackage ReadAccompany(FalloutPluginRecord record) => Read(record, true);

    private static FalloutFollowPackage Read(FalloutPluginRecord record, bool accompany)
    {
        if (record.Signature != "PACK") throw new InvalidDataException("Follow source is not PACK.");
        var fields = record.ReadSubrecords().ToArray();
        ReadOnlyMemory<byte> Required(string name, int size)
        {
            var found = fields.Where(field => field.Signature == name).ToArray();
            return found.Length == 1 && found[0].Data.Length == size ? found[0].Data :
                throw new InvalidDataException($"Follow package {record.FormKey} has invalid {name}.");
        }
        var data = FalloutPackageData.Read(record);
        if (data.Procedure != (accompany ? 7 : 1)) throw new InvalidDataException("Package has another companion procedure.");
        if (accompany)
        {
            // Continue During Combat is retained but has no effect for
            // Accompany. Defensive combat is handled by the engagement owner.
            const uint supported = 0x1000 | 0x2000 | FalloutScriptPackage.HeadTrackingOffFlag |
                0x400000 | 0x04000000;
            if ((data.Flags & ~supported) != 0 || data.SpecificFlags is not (null or 0) ||
                (data.Flags & 0x1000) == 0 && data.BehaviorFlags != 0 ||
                fields.Any(field => field.Signature is "PTD2" or "PKFD"))
                throw new NotSupportedException("Accompany has additional unowned behavior or target inputs.");
            if (FalloutScriptPackage.Read(record).Idles.Count != 0)
                throw new NotSupportedException("Accompany has an unowned waiting-idle collection.");
        }
        if (fields.Any(field => field.Signature is "PLDT" or "PLD2"))
            throw new NotSupportedException("Follow start/end locations require their travel/completion owner.");
        var target = Required("PTDT", 16).Span;
        if (BinaryPrimitives.ReadInt32LittleEndian(target) != 0)
            throw new NotSupportedException("Follow target requires its non-reference selection owner.");
        var key = record.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(target[4..]));
        var distance = BinaryPrimitives.ReadInt32LittleEndian(target[8..]);
        if (distance <= 0) throw new InvalidDataException("Follow distance must be positive.");
        return new(record.FormKey, key, distance, data.Flags, accompany);
    }
}
