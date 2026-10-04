using System.Buffers.Binary;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutEditorTravelPackage(FalloutFormKey Form, uint Flags, int Radius)
{
    internal bool Running => (Flags & 0x2000) != 0;
    internal bool WeaponDrawn => (Flags & 0x800000) != 0;
    internal bool MustComplete => (Flags & 4) != 0;

    internal static FalloutEditorTravelPackage Read(FalloutPluginRecord record)
    {
        var declaration = FalloutScriptPackage.Read(record);
        if (declaration.Procedure != 6 || declaration.LocationType != 3)
            throw new InvalidDataException("Package is not Travel to editor location.");
        var data = record.ReadSubrecords().Single(field => field.Signature == "PKDT").Data.Span;
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(data);
        const uint supported = 2 | 4 | 0x1000 | 0x2000 | 0x800000 | FalloutScriptPackage.HeadTrackingOffFlag;
        if ((flags & ~supported) != 0 || BinaryPrimitives.ReadUInt16LittleEndian(data[6..]) != 0 ||
            BinaryPrimitives.ReadUInt16LittleEndian(data[8..]) != 0 ||
            record.ReadSubrecords().Any(field => field.Signature is "PLD2" or "PTDT" or "PTD2"))
            throw new NotSupportedException("Editor travel needs its additional behavior, search or target owner.");
        return new(record.FormKey, flags, declaration.LocationRadius);
    }

    internal FalloutEditorTravelProgress Start(FalloutReferenceWorld world, FalloutFormKey actor)
    {
        var source = world.EditorPlacement(actor);
        return new(source.Cell, source.Position, false);
    }

    internal void Validate(FalloutReferenceWorld world, FalloutFormKey actor, FalloutEditorTravelProgress progress)
    {
        progress.Validate();
        var source = Start(world, actor);
        if (source.Cell != progress.Cell || !source.Location.SequenceEqual(progress.Location))
            throw new InvalidDataException("Saved editor travel destination differs from its source actor.");
    }
}
