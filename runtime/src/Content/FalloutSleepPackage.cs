using System.Buffers.Binary;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutSleepCellSource(FalloutFormKey Cell,
    IReadOnlyDictionary<FalloutFormKey, FalloutPlacedReference> Beds) : IFalloutSourceIndex;

internal sealed record FalloutSleepPackage(FalloutFormKey Form, uint Flags, FalloutPackageLocation Location)
{
    internal bool Running => (Flags & 0x2000) != 0;
    internal static FalloutSleepPackage Read(FalloutPluginRecord record)
    {
        var source = FalloutScriptPackage.Read(record);
        var data = FalloutPackageData.Read(record);
        if (source.Procedure != 4 || source.LocationDeclaration is not { Type: 0 or 1 or 2 or 3 } location)
            throw new NotSupportedException("Sleep requires its standard source location.");
        const uint supported = 0x1000 | 0x2000 | 0x20000 | FalloutScriptPackage.HeadTrackingOffFlag |
            FalloutScriptPackage.WeaponsUnequippedFlag | FalloutScriptPackage.DoorStartEndFlags;
        if ((data.Flags & ~supported) != 0 || (data.Flags & 0x1000) == 0 && data.BehaviorFlags != 0 ||
            data.SpecificFlags is not (null or 0) || record.ReadSubrecords().Any(field => field.Signature is "PLD2" or "PTDT" or "PTD2"))
            throw new NotSupportedException("Sleep has additional unowned behavior or target inputs.");
        if ((data.Flags & FalloutScriptPackage.DoorStartEndFlags) != 0)
        {
            // Only the source-proven empty door-work case is owned. Keep
            // event/door ordering explicit when authored results could change
            // doors or actor placement during this lifecycle transition.
            if (source.Events.Values.Any(idle => idle is not null))
                throw new NotSupportedException("Sleep door actions with event idles require their ordered execution owner.");
            foreach (var program in source.EventPrograms.Values) program.RequireEmptyScript();
        }
        if (location.Type == 1 && location.Form is null)
            throw new InvalidDataException("In-cell Sleep has no source CELL.");
        return new(record.FormKey, data.Flags, location);
    }

    internal (FalloutFormKey Cell, float[]? Center) Scope(FalloutReferenceWorld world, FalloutFormKey actor)
    {
        if (Location.Type == 1) return (Location.Form!.Value, null);
        FalloutReferencePlacement placement = Location.Type switch
        {
            0 => world.Placement(Location.Form ?? throw new InvalidDataException("Sleep reference location is absent.")),
            2 => world.ActorPackageStartLocation(actor) is { } start ? new(start.Cell, start.Position, [0, 0, 0]) :
                throw new NotSupportedException("Current-location Sleep has no observed package-start anchor."),
            3 => world.EditorPlacement(actor),
            _ => throw new NotSupportedException("Sleep location is unowned.")
        };
        placement.Validate();
        return (placement.Cell, placement.Position);
    }

    internal IReadOnlyList<FalloutPlacedReference> Candidates(FalloutPluginStack records, FalloutReferenceWorld world,
        FalloutFormKey actor)
    {
        var (cell, center) = Scope(world, actor);
        return Beds(records, cell).Values.Where(reference => world.ActorMayUseFurniture(actor, reference.FormKey) &&
            world.Placement(reference.FormKey).Cell == cell &&
            (center is null || Location.Radius == 0 || Within(world.Placement(reference.FormKey).Position, center, Location.Radius)))
            .ToArray();
    }

    internal void Validate(FalloutPluginStack records, FalloutReferenceWorld world, FalloutFormKey actor,
        FalloutActorSleepProgress progress, FalloutSleepCellSource? source = null)
    {
        progress.Validate();
        var (cell, center) = Scope(world, actor);
        if (records.GetEffective(world.Get(actor).Base).Signature != "NPC_" || progress.Cell != cell ||
            !(source is { } retained && retained.Cell == cell ? retained.Beds : Source(records, cell).Beds).ContainsKey(progress.Bed) ||
            !world.ActorMayUseFurniture(actor, progress.Bed) ||
            world.Placement(progress.Bed).Cell != cell ||
            center is not null && Location.Radius != 0 && !Within(world.Placement(progress.Bed).Position, center, Location.Radius))
            throw new InvalidDataException("Sleep bed intent differs from its admitted source location, ownership or enable state.");
    }

    private static IReadOnlyDictionary<FalloutFormKey, FalloutPlacedReference> Beds(FalloutPluginStack records, FalloutFormKey cell)
        => Source(records, cell).Beds;

    internal FalloutSleepCellSource Source(FalloutPluginStack records, FalloutReferenceWorld world, FalloutFormKey actor)
        => Source(records, Scope(world, actor).Cell);

    private static FalloutSleepCellSource Source(FalloutPluginStack records, FalloutFormKey cell)
        => records.ReadSourceIndex<FalloutSleepCellSource>((owner, _) =>
        {
            var scene = FalloutCellSceneReader.Read(owner, cell);
            return new(cell, scene.References.Where(reference => scene.BaseObjects[reference.Base].Signature == "FURN" &&
                IsBed(owner, reference.Base)).ToDictionary(reference => reference.FormKey));
        }, scope: cell);

    private static bool Within(float[] point, float[] center, int radius) => Enumerable.Range(0, 3)
        .Sum(index => Math.Pow((double)point[index] - center[index], 2)) <= (double)radius * radius;

    private static bool IsBed(FalloutPluginStack records, FalloutFormKey key)
    {
        var fields = records.GetEffective(key).ReadSubrecords().Where(field => field.Signature == "MNAM").ToArray();
        if (fields.Length != 1 || fields[0].Data.Length != 4) throw new InvalidDataException("Sleep furniture has invalid marker flags.");
        return (BinaryPrimitives.ReadUInt32LittleEndian(fields[0].Data.Span) & 0xc0000000u) == 0x80000000u;
    }
}
