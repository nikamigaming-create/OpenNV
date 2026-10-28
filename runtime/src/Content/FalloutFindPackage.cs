using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Content;

// This is the source declaration shared by Find consumers. Reading it does not
// admit a target category, package flag, acquisition or movement behavior.
internal sealed record FalloutFindPackage(FalloutScriptPackage Source, FalloutPackageData Data, FalloutPackageTarget Target)
{
    internal bool AllowKilling => (Data.SpecificFlags.GetValueOrDefault() & 0x0200) != 0;
    internal bool AllowFalls => (Data.Flags & 0x00080000) != 0;

    internal static FalloutFindPackage Read(FalloutPluginRecord record)
    {
        var data = FalloutPackageData.Read(record);
        if (data.Procedure != 0) throw new InvalidDataException("Find source is not a Find procedure.");
        return new(FalloutScriptPackage.Read(record), data, FalloutPackageTarget.Read(record));
    }

    // The actor's retained start is shared with the Sandbox location owner.
    // Other source types are explicit refusals until their search owners exist.
    internal FalloutReferencePlacement Location(FalloutPluginStack records, FalloutReferenceWorld world, FalloutFormKey actor)
    {
        if (records.GetEffective(world.Get(actor).Base).Signature is not ("NPC_" or "CREA"))
            throw new InvalidDataException("Find caller is not an actor.");
        FalloutReferencePlacement placement;
        if (Source.LocationType == 2)
        {
            var start = world.ActorPackageStartLocation(actor) ??
                throw new NotSupportedException("Current-location Find has no retained actor package-start location.");
            start.Validate();
            placement = new(start.Cell, (float[])start.Position.Clone(), [0, 0, 0]);
        }
        else if (Source.LocationType == 3) placement = world.EditorPlacement(actor);
        else if (Source.LocationType == 0 && Source.LocationReference is { } reference) placement = world.Placement(reference);
        else throw new NotSupportedException("Find source location requires its search-location owner.");
        placement.Validate();
        if (records.GetEffective(placement.Cell).Signature != "CELL")
            throw new InvalidDataException("Find location has no source CELL.");
        return placement;
    }
}
