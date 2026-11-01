using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutFollowPackage(FalloutFormKey Form, FalloutFormKey Target, int Distance, uint Flags,
    bool Accompany = false, int TargetKind = 0)
{
    internal bool DefensiveCombat => (Flags & 0x400000) != 0;
    internal bool RequiresDefensiveCombatOwner => !Accompany && DefensiveCombat;
    internal bool ContinueIfPlayerNear => (Flags & 0x200) != 0;
    internal bool RetainsSelection(bool playerInSameCell, bool forced) =>
        !forced && ContinueIfPlayerNear && playerInSameCell;
    internal FalloutFollowPackage ResolveTarget(Func<FalloutFormKey?> linked)
    {
        if (TargetKind == 0) return this;
        if (TargetKind != 3) throw new NotSupportedException("Follow target has no admitted selection owner.");
        var actual = linked() ?? throw new NotSupportedException("Linked-reference Follow has no current source XLKR/runtime link owner.");
        if (actual.ObjectId == 0 || string.IsNullOrWhiteSpace(actual.OwnerPlugin))
            throw new InvalidDataException("Linked-reference Follow resolved an invalid target identity.");
        return this with { Target = actual };
    }
    internal bool Running => (Flags & 0x2000) != 0;
    internal bool AllowsUnprovokedCombat(FalloutFormKey? attackedTarget)
    {
        if (RequiresDefensiveCombatOwner)
            throw new NotSupportedException($"Follow package {Form} DefensiveCombat requires its native combat-policy owner.");
        return !Accompany || !DefensiveCombat || attackedTarget == Target;
    }
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
            const uint supported = 0x200 | 0x1000 | 0x2000 | FalloutScriptPackage.HeadTrackingOffFlag |
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
        var kind = BinaryPrimitives.ReadInt32LittleEndian(target);
        if (kind is not (0 or 3)) throw new NotSupportedException("Follow target requires its object selection owner.");
        // Linked declarations have an unused target word, not an authored form.
        // Every resident owner must resolve this declaration before validation/use.
        var key = kind == 0 ? record.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(target[4..])) : default;
        var distance = BinaryPrimitives.ReadInt32LittleEndian(target[8..]);
        if (distance <= 0) throw new InvalidDataException("Follow distance must be positive.");
        return new(record.FormKey, key, distance, data.Flags, accompany, kind);
    }
}
