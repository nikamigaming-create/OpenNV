using System.Buffers.Binary;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutEatPackage(FalloutFormKey Form, uint Flags, FalloutPackageLocation Location,
    ushort BehaviorFlags, ushort? SpecificFlags, FalloutPackageTarget? FoodTarget, bool Creature = false, FalloutPackageLocation? SearchLocation = null)
{
    internal bool Running => (Flags & 0x2000) != 0;
    internal bool WeaponDrawn => (Flags & 0x800000) != 0;

    internal static FalloutEatPackage Read(FalloutPluginRecord record, bool creature = false)
    {
        var declaration = FalloutScriptPackage.Read(record);
        var data = FalloutPackageData.Read(record);
        var supported = 0x2000u | FalloutScriptPackage.HeadTrackingOffFlag | FalloutScriptPackage.WeaponsUnequippedFlag |
            (creature ? 6u | FalloutScriptPackage.AllowFallsFlag : 0u);
        if ((data.Flags & ~supported) != 0 || data.BehaviorFlags != 0 ||
            (creature ? data.SpecificFlags is not (null or 0 or 0x200) : data.SpecificFlags is not (null or 0)))
            throw new NotSupportedException("Eat has unowned package behavior or specific flags.");
        if (declaration.Procedure != 3 || declaration.LocationDeclaration is not { Type: 0 or 1 or 2 or 3 } location)
            throw new NotSupportedException("Eat requires its standard source location.");
        var fields = record.ReadSubrecords().ToArray();
        if (fields.Any(field => field.Signature == "PTD2") || !creature && fields.Any(field => field.Signature == "PLD2"))
            throw new NotSupportedException("Eat has additional location or target inputs without an execution owner.");
        var searchLocation = creature ? CreatureSearchLocation(record) : null;
        var target = fields.Any(field => field.Signature == "PTDT") ? FalloutPackageTarget.Read(record) : null;
        if (target is { AuxiliaryBits: not 0 } || target is { CountOrDistance: not 0 })
            throw new NotSupportedException("Eat target has unowned auxiliary or quantity inputs.");
        if (target is { Type: 0 })
            throw new NotSupportedException("Eat reference target requires its exact source-reference ownership and acquisition.");
        if (target is not null && target is not ({ Type: 1, Form: not null } or { Type: 2, RawTarget: 0 or 18 }))
            throw new NotSupportedException("Eat has an unowned food target category.");
        if (location.Type == 1 && location.Form is null)
            throw new InvalidDataException("In-cell Eat has no source CELL.");
        if (creature && (location.Type != 2 || (data.Flags & 4) == 0 || FalloutPackageSchedule.Read(record).Duration == 0))
            throw new NotSupportedException("Creature Eat requires its qualified current-location timed MustComplete owner.");
        return new(record.FormKey, data.Flags, location, data.BehaviorFlags, data.SpecificFlags, target, creature, searchLocation);
    }

    private static FalloutPackageLocation? CreatureSearchLocation(FalloutPluginRecord record)
    {
        var fields = record.ReadSubrecords().Where(field => field.Signature == "PLD2").ToArray();
        if (fields.Length == 0) return null;
        if (fields.Any(field => field.Data.Length != 12) || fields.Skip(1).Any(field => !field.Data.Span.SequenceEqual(fields[0].Data.Span)))
            throw new NotSupportedException("Creature Eat secondary locations require complete identical declarations.");
        var bytes = fields[0].Data.Span;
        var type = BinaryPrimitives.ReadInt32LittleEndian(bytes);
        var raw = BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]);
        var radius = BinaryPrimitives.ReadInt32LittleEndian(bytes[8..]);
        var form = record.Plugin.AdjustOptionalFormId(raw);
        if (type != 0 || form is null || radius < 0)
            throw new NotSupportedException("Creature Eat secondary search location is not a qualified near-reference declaration.");
        // This is the Character food-search region. The admitted Creature
        // used-item producer bypasses that search and never navigates here.
        // Identical copies preserve the same declaration without choosing an
        // unqualified precedence rule for conflicting source fields.
        return new(type, raw, form, radius);
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
        if (progress.Cell != cell || records.GetEffective(world.Get(actor).Base).Signature != (Creature ? "CREA" : "NPC_") ||
            (progress.Creature is not null) != Creature)
            throw new InvalidDataException("Eat continuation differs from its source location or actor owner.");
        progress.Creature?.UsedItem?.Validate(records, this);
        if (SearchLocation is { Form: { } search } && records.GetEffective(search).Signature != "REFR")
            throw new NotSupportedException("Creature Eat secondary search anchor requires its qualified placed-reference source.");
        if (progress.Seat is { } seat && (!world.SameTravelRegion(world.Placement(seat).Cell, cell) ||
            records.GetEffective(world.Get(seat).Base).Signature != "FURN" || !world.ActorMayUseFurniture(actor, seat)))
            throw new InvalidDataException("Eat seat intent differs from its source furniture, region or permission.");
    }

    internal bool TimedMustCompleteExpired(FalloutPluginRecord package, float hour, float startedHour)
    {
        if (!Creature || !float.IsFinite(hour) || hour is < 0 or >= 24 || !float.IsFinite(startedHour) || startedHour is < 0 or >= 24)
            throw new InvalidDataException("Creature Eat expiry has no valid source clock.");
        var schedule = FalloutPackageSchedule.Read(package);
        var end = checked((schedule.Hour == -1 ? (int)startedHour : schedule.Hour) + schedule.Duration);
        // The native timed MustComplete expression wraps by23 once. Ordinary
        // schedule selection remains an independent calendar owner.
        if (end > 23) end -= 23;
        else if (hour < schedule.Hour) return true;
        return (int)hour >= end;
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
