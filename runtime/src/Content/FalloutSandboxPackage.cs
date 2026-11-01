using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutSandboxPackage(FalloutFormKey Form, uint Flags, int LocationType,
    FalloutFormKey? Reference, int Radius, FalloutSandboxActivities Activities)
{
    internal const string CaptureBlockerPrefix = "Sandbox continuation: ";
    internal bool Running => (Flags & 0x2000) != 0;
    internal bool WeaponDrawn => (Flags & 0x800000) != 0;
    internal bool DefensiveCombat => (Flags & 0x400000) != 0;
    internal bool ContinueIfPlayerNear => (Flags & 0x200) != 0;
    internal bool RetainsSelection(bool playerInSameCell, bool forced) =>
        !forced && ContinueIfPlayerNear && playerInSameCell;

    internal static FalloutSandboxPackage Read(FalloutPluginRecord record)
    {
        var source = FalloutScriptPackage.Read(record);
        var activities = FalloutSandboxActivities.Read(record);
        if (source.LocationType is not (0 or 2 or 3 or 6))
            throw new NotSupportedException("Sandbox needs its reference, current, editor or linked-reference location owner.");
        var data = FalloutPackageData.Read(record);
        const uint supported = 0x200 | 0x1000 | 0x2000 | 0x400000 | 0x800000 | FalloutScriptPackage.HeadTrackingOffFlag |
            FalloutScriptPackage.WeaponsUnequippedFlag;
        if ((data.Flags & ~supported) != 0)
            throw new NotSupportedException($"Sandbox package flags 0x{data.Flags & ~supported:x8} have no execution owner.");
        if ((data.Flags & 0x1000) == 0 && data.BehaviorFlags != 0)
            throw new NotSupportedException($"Sandbox behavior flags 0x{data.BehaviorFlags:x4} have no execution owner.");
        if (source.Idles.Count != 0)
            throw new NotSupportedException("Sandbox package idle collection has no execution owner.");
        if (record.ReadSubrecords().Any(field => field.Signature is "PLD2" or "PTDT" or "PTD2"))
            throw new NotSupportedException("Sandbox has additional location or target inputs without an execution owner.");
        return new(record.FormKey, data.Flags, source.LocationType.Value, source.LocationReference, source.LocationRadius, activities);
    }

    internal FalloutTravelProgress Location(FalloutPluginStack records, FalloutReferenceWorld world, FalloutFormKey actor)
    {
        if (LocationType == 6)
        {
            var linked = world.GetLinkedRef(actor) ??
                throw new NotSupportedException("Linked-reference Sandbox has no current source link owner.");
            return new FalloutTravelPackage(Form, Flags, 0, linked, Radius).Start(records, world, actor);
        }
        if (LocationType != 2) return new FalloutTravelPackage(Form, Flags, LocationType, Reference, Radius).Start(records, world, actor);
        var start = world.ActorPackageStartLocation(actor) ??
            throw new NotSupportedException("Current-location Sandbox has no retained actor package-start location.");
        start.Validate();
        return new(start.Cell, start.Position, false);
    }

    internal void RequireRetainedStart(FalloutPluginStack records, FalloutReferenceInstance actor)
    {
        if (LocationType != 2) return;
        var start = actor.PackageStartLocation ??
            throw new InvalidDataException("Current-location Sandbox has no retained actor start location.");
        start.Validate();
        if (records.GetEffective(start.Cell).Signature != "CELL")
            throw new InvalidDataException("Current-location Sandbox has no source CELL.");
    }
}
