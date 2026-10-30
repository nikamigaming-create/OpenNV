using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutEatPackage(FalloutFormKey Form, uint Flags, FalloutPackageLocation Location,
    ushort BehaviorFlags, ushort? SpecificFlags, FalloutPackageTarget? FoodTarget)
{
    internal bool Running => (Flags & 0x2000) != 0;
    internal bool WeaponDrawn => (Flags & 0x800000) != 0;

    internal static FalloutEatPackage Read(FalloutPluginRecord record)
    {
        var declaration = FalloutScriptPackage.Read(record);
        var data = FalloutPackageData.Read(record);
        const uint supported = 0x2000 | FalloutScriptPackage.HeadTrackingOffFlag | FalloutScriptPackage.WeaponsUnequippedFlag;
        if ((data.Flags & ~supported) != 0 || data.BehaviorFlags != 0 || data.SpecificFlags is not (null or 0))
            throw new NotSupportedException("Eat has unowned package behavior or specific flags.");
        if (declaration.Procedure != 3 || declaration.LocationDeclaration is not { Type: 0 or 1 or 2 or 3 } location)
            throw new NotSupportedException("Eat requires its standard source location.");
        var fields = record.ReadSubrecords().ToArray();
        if (fields.Any(field => field.Signature is "PLD2" or "PTD2"))
            throw new NotSupportedException("Eat has additional location or target inputs without an execution owner.");
        var target = fields.Any(field => field.Signature == "PTDT") ? FalloutPackageTarget.Read(record) : null;
        if (target is { AuxiliaryBits: not 0 } || target is { CountOrDistance: not 0 })
            throw new NotSupportedException("Eat target has unowned auxiliary or quantity inputs.");
        if (target is { Type: 0 })
            throw new NotSupportedException("Eat reference target requires its exact source-reference ownership and acquisition.");
        if (target is not null && target is not ({ Type: 1, Form: not null } or { Type: 2, RawTarget: 0 or 18 }))
            throw new NotSupportedException("Eat has an unowned food target category.");
        if (location.Type == 1 && location.Form is null)
            throw new InvalidDataException("In-cell Eat has no source CELL.");
        return new(record.FormKey, data.Flags, location, data.BehaviorFlags, data.SpecificFlags, target);
    }

    internal bool AcceptsFood(FalloutPluginStack records, FalloutReferenceWorld world, FalloutFormKey food)
    {
        if (!world.IsActorFood(food)) return false;
        if (FoodTarget is null || FoodTarget is { Type: 2, RawTarget: 0 or 18 }) return true;
        if (FoodTarget is { Type: 1, Form: { } required })
        {
            if (records.GetEffective(required).Signature == "FLST")
                throw new NotSupportedException("Eat FormList target requires its qualified target-selection owner.");
            return food == required;
        }
        if (FoodTarget is { Type: 0 })
            throw new NotSupportedException("Eat cannot replace a required food reference with an inventory base match.");
        throw new NotSupportedException("Eat food target category has no admitted inventory matcher.");
    }

    internal (FalloutFormKey Cell, float[]? Center) Scope(FalloutReferenceWorld world, FalloutFormKey actor)
    {
        if (Location.Type == 1) return (Location.Form!.Value, null);
        var placement = Location.Type switch
        {
            0 => world.Placement(Location.Form ?? throw new InvalidDataException("Eat reference location is absent.")),
            2 => world.ActorPackageStartLocation(actor) is { } start ? new FalloutReferencePlacement(start.Cell, start.Position, [0, 0, 0]) :
                throw new NotSupportedException("Current-location Eat has no observed package-start anchor."),
            3 => world.EditorPlacement(actor),
            _ => throw new NotSupportedException("Eat location is unowned.")
        };
        placement.Validate();
        return (placement.Cell, placement.Position);
    }

    internal void Validate(FalloutPluginStack records, FalloutReferenceWorld world, FalloutFormKey actor,
        FalloutActorEatPackageProgress progress)
    {
        progress.Validate();
        var (cell, _) = Scope(world, actor);
        if (progress.Cell != cell || records.GetEffective(world.Get(actor).Base).Signature != "NPC_")
            throw new InvalidDataException("Eat continuation differs from its source location or actor owner.");
        if (progress.Seat is { } seat && (!world.SameTravelRegion(world.Placement(seat).Cell, cell) ||
            records.GetEffective(world.Get(seat).Base).Signature != "FURN" || !world.ActorMayUseFurniture(actor, seat)))
            throw new InvalidDataException("Eat seat intent differs from its source furniture, region or permission.");
    }

    internal bool ContainsChair(FalloutReferenceWorld world, FalloutFormKey actor, FalloutFormKey chair)
    {
        var (cell, center) = Scope(world, actor);
        var placement = world.Placement(chair);
        if (!world.SameTravelRegion(cell, placement.Cell)) return false;
        if (center is null || Location.Type == 0 && Location.Form == chair) return true;
        return Enumerable.Range(0, 3).Sum(index => Math.Pow((double)placement.Position[index] - center[index], 2)) <=
            (double)Location.Radius * Location.Radius;
    }
}
