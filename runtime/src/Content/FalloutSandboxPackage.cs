using System.Buffers.Binary;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutSandboxArea(FalloutFormKey Cell, float[]? Center, int Radius)
{
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Cell.OwnerPlugin) || Cell.ObjectId == 0 || Radius < 0 ||
            Center is not null && (Center.Length != 3 || Center.Any(value => !float.IsFinite(value))))
            throw new InvalidDataException("Sandbox source area is invalid.");
    }
    internal FalloutSandboxArea Copy() => this with { Center = Center is null ? null : (float[])Center.Clone() };
}

internal sealed record FalloutSandboxPackage(FalloutFormKey Form, uint Flags, ushort Vetoes,
    int LocationType, FalloutFormKey? Location, int Radius)
{
    internal bool Running => (Flags & 0x2000) != 0;
    internal bool WeaponDrawn => (Flags & 0x800000) != 0;
    internal bool NoEating => (Vetoes & 1) != 0;
    internal bool NoSleeping => (Vetoes & 2) != 0;
    internal bool NoConversation => (Vetoes & 4) != 0;
    internal bool NoIdleMarkers => (Vetoes & 8) != 0;
    internal bool NoFurniture => (Vetoes & 16) != 0;
    internal bool NoWandering => (Vetoes & 32) != 0;

    internal static FalloutSandboxPackage Read(FalloutPluginRecord record)
    {
        var common = FalloutScriptPackage.Read(record);
        var data = FalloutPackageData.Read(record);
        if (common.Procedure != 12) throw new InvalidDataException("Selected source package is not Sandbox.");
        const uint supported = 2 | 4 | 0x1000 | 0x2000 | 0x800000 | 0x01000000 |
            FalloutScriptPackage.HeadTrackingOffFlag | FalloutScriptPackage.WeaponsUnequippedFlag;
        if ((data.Flags & ~supported) != 0 || (data.Flags & 0x1000) == 0 && data.BehaviorFlags != 0 ||
            (data.SpecificFlags.GetValueOrDefault() & ~63) != 0)
            throw new NotSupportedException("Sandbox has unowned package flags or optional behavior consumers.");
        var fields = record.ReadSubrecords().ToArray();
        if (fields.Any(field => field.Signature is "PLD2" or "PTDT" or "PTD2"))
            throw new NotSupportedException("Sandbox secondary location/target requires its independent owner.");
        var locations = fields.Where(field => field.Signature == "PLDT").ToArray();
        if (locations.Length != 1 || locations[0].Data.Length != 12)
            throw new InvalidDataException("Sandbox requires one complete source location declaration.");
        var value = locations[0].Data.Span;
        var type = BinaryPrimitives.ReadInt32LittleEndian(value);
        var raw = BinaryPrimitives.ReadUInt32LittleEndian(value[4..]);
        var radius = BinaryPrimitives.ReadInt32LittleEndian(value[8..]);
        if (type is not (0 or 1 or 2 or 3))
            throw new NotSupportedException("Sandbox object-type/linked/package location has no source resolver.");
        if (radius < 0) throw new InvalidDataException("Sandbox location radius is negative.");
        var location = type is 0 or 1 ? record.Plugin.AdjustOptionalFormId(raw) ??
            throw new InvalidDataException("Sandbox source reference or CELL location is absent.") : (FalloutFormKey?)null;
        return new(record.FormKey, data.Flags, data.SpecificFlags.GetValueOrDefault(), type, location, radius);
    }

    internal FalloutSandboxArea Resolve(FalloutPluginStack records, FalloutReferenceWorld world, FalloutFormKey actor)
    {
        if (records.GetEffective(world.Get(actor).Base).Signature is not ("NPC_" or "CREA"))
            throw new InvalidDataException("Sandbox source caller is not an actor.");
        if (LocationType == 1)
        {
            if (records.GetEffective(Location!.Value).Signature != "CELL")
                throw new InvalidDataException("Sandbox InCell location is not a winning CELL.");
            // InCell constrains the area; it does not invent a center marker.
            return new(Location.Value, null, Radius);
        }
        var placement = LocationType switch
        {
            0 => world.Placement(Location!.Value),
            2 => world.Placement(actor),
            3 => world.EditorPlacement(actor),
            _ => throw new InvalidDataException("Sandbox location lost its source type.")
        };
        placement.Validate();
        if (LocationType == 0 && !world.IsEnabled(Location!.Value))
            throw new NotSupportedException("Sandbox location has no effective enabled source reference.");
        return new(placement.Cell, (float[])placement.Position.Clone(), Radius);
    }
}
