using System.Buffers.Binary;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutTravelPackage(FalloutFormKey Form, uint Flags, int LocationType,
    FalloutFormKey? Reference, int Radius)
{
    internal bool Running => (Flags & 0x2000) != 0;
    internal bool MustReach => (Flags & 6) != 0;
    internal bool OncePerDay => (Flags & 0x400) != 0;

    internal static FalloutTravelPackage Read(FalloutPluginRecord record, bool ownsIdleCollection = false)
    {
        var source = FalloutScriptPackage.Read(record);
        if (source.Procedure != 6 || source.LocationType is not (0 or 3))
            throw new NotSupportedException("Travel requires its reference-marker or editor-location owner.");
        var data = record.ReadSubrecords().Single(field => field.Signature == "PKDT").Data.Span;
        const uint supported = 2 | 4 | 0x400 | 0x1000 | 0x2000;
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(data);
        // Skip Fallout Behavior disables the optional behavior lane. Retained
        // declarations in that lane do not add a second travel procedure.
        if ((flags & ~supported) != 0 ||
            (flags & 0x1000) == 0 && BinaryPrimitives.ReadUInt16LittleEndian(data[6..]) != 0 ||
            BinaryPrimitives.ReadUInt16LittleEndian(data[8..]) != 0 ||
            record.ReadSubrecords().Any(field => field.Signature is "PLD2" or "PTDT" or "PTD2") ||
            !ownsIdleCollection && source.Idles.Count != 0)
            throw new NotSupportedException("Travel has additional unowned behavior, target or idle-collection inputs.");
        return new(record.FormKey, flags, source.LocationType.Value,
            source.LocationReference, source.LocationRadius);
    }

    internal FalloutTravelProgress Start(FalloutPluginStack records, FalloutReferenceWorld world, FalloutFormKey actor)
    {
        if (records.GetEffective(world.Get(actor).Base).Signature is not ("NPC_" or "CREA"))
            throw new InvalidDataException("Travel caller is not an actor.");
        FalloutReferencePlacement location;
        if (LocationType == 3) location = world.EditorPlacement(actor);
        else
        {
            var target = Reference ?? throw new InvalidDataException("Travel reference location is absent.");
            var state = world.Get(target);
            var form = records.GetEffective(state.Base);
            if (state.Deleted || state.DeletePending ||
                !FalloutNewVegasBuiltinForms.IsInternalStatic(form.Signature, records.RuntimeFormId(form.FormKey)))
                throw new NotSupportedException("Travel destination requires its non-marker interaction owner.");
            location = world.Placement(target);
        }
        location.Validate();
        return new(location.Cell, (float[])location.Position.Clone(), false);
    }

    internal void Validate(FalloutPluginStack records, FalloutReferenceWorld world, FalloutFormKey actor,
        FalloutTravelProgress progress)
    {
        progress.Validate();
        var source = Start(records, world, actor);
        if (source.Cell != progress.Cell || !source.Location.SequenceEqual(progress.Location))
            throw new InvalidDataException("Saved Travel location differs from its winning reference owner.");
        if (records.GetEffective(world.Get(actor).Base).Signature == "NPC_")
        {
            if (progress.NavigationSha256 is null || !progress.Complete &&
                progress.RouteWaypoints is not { Length: > 0 } && progress.RouteFailure is null)
                throw new NotSupportedException("Saved NPC marker Travel lacks its native route cursor and source identity.");
            var navigation = CellNavigationGraph.LoadOwned(records, progress.Cell);
            if (!progress.NavigationSha256.Equals(navigation.SourceSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Saved NPC Travel route differs from its winning NAVM source.");
        }
    }
}
