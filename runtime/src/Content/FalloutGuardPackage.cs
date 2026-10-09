using System.Buffers.Binary;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutGuardPackage(FalloutFormKey Form, uint Flags, int LocationType,
    FalloutFormKey? Reference, int Radius, FalloutFormKey? Target, int? TargetRadius)
{
    internal bool Running => (Flags & 0x2000) != 0;
    internal bool WeaponDrawn => (Flags & 0x800000) != 0;
    internal bool WarnAndAttack => (Flags & 0x10000000) == 0;
    internal bool ContinueCombat => (Flags & 0x04000000) != 0;

    internal static FalloutGuardPackage Read(FalloutPluginRecord record, bool ownsIdleCollection = false)
    {
        var source = FalloutScriptPackage.Read(record);
        if (source.Procedure != 14 || source.LocationType is not (0 or 3))
            throw new NotSupportedException("Guard requires its reference-marker or editor-location owner.");
        var fields = record.ReadSubrecords().ToArray();
        var data = FalloutPackageData.Read(record);
        var flags = data.Flags;
        const uint supported = 0x1000 | 0x2000 | 0x800000 | 0x04000000 | 0x10000000;
        if ((flags & ~supported) != 0 || data.BehaviorFlags != 0 || data.SpecificFlags is not (null or 0) || !ownsIdleCollection && source.Idles.Count != 0 ||
            fields.Any(field => field.Signature is "PLD2" or "PTD2"))
            throw new NotSupportedException("Guard has additional unowned behavior or idle inputs.");
        var targets = fields.Where(field => field.Signature == "PTDT").ToArray();
        FalloutFormKey? target = null;
        int? targetRadius = null;
        if (targets.Length != 0)
        {
            if (targets.Length != 1 || targets[0].Data.Length != 16)
                throw new InvalidDataException("Guard target has an invalid extent.");
            var value = targets[0].Data.Span;
            if (BinaryPrimitives.ReadInt32LittleEndian(value) != 0 || BinaryPrimitives.ReadUInt32LittleEndian(value[12..]) != 0)
                throw new NotSupportedException("Guard target selection requires its non-reference owner.");
            target = record.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(value[4..])) ??
                throw new InvalidDataException("Guard target reference is absent.");
            targetRadius = BinaryPrimitives.ReadInt32LittleEndian(value[8..]);
            if (targetRadius < 0) throw new InvalidDataException("Guard target radius is negative.");
        }
        return new(record.FormKey, flags, source.LocationType.Value, source.LocationReference,
            source.LocationRadius, target, targetRadius);
    }

    internal FalloutTravelProgress Start(FalloutPluginStack records, FalloutReferenceWorld world, FalloutFormKey actor)
    {
        // Guard first approaches its location. The location radius belongs to
        // later wandering; it is not an early arrival tolerance for this leg.
        var approach = new FalloutTravelPackage(Form, Flags, LocationType, Reference, 0);
        return approach.Start(records, world, actor);
    }

    internal void Validate(FalloutPluginStack records, FalloutReferenceWorld world, FalloutFormKey actor,
        FalloutTravelProgress progress)
    {
        progress.Validate();
        var source = Start(records, world, actor);
        if (progress.Cell != source.Cell || !progress.Location.SequenceEqual(source.Location))
            throw new InvalidDataException("Saved Guard location differs from its winning reference owner.");
        if (Target is { } target) _ = world.Get(target);
    }

    internal void RequireStationaryBehavior()
    {
        if (Radius != 0) throw new NotSupportedException("Guard reached its location; radial wandering needs its source clock and destination owner.");
        if (WarnAndAttack) throw new NotSupportedException("Guard reached its location; intrusion warnings and attacks need their source owner.");
        if (ContinueCombat) throw new NotSupportedException("Guard reached its location; combat confinement needs its source owner.");
    }
}
