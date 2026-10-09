using System.Buffers.Binary;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutFurnitureSeat(FalloutFormKey Furniture, int Index, int MarkerId,
    FalloutNifFurniturePosition Marker, float[] PlacementOffset, float HeadingDelta,
    FalloutPlayerFurnitureKind Kind = FalloutPlayerFurnitureKind.Sitting);

internal static class FalloutFurnitureSource
{
    internal static FalloutPlayerFurnitureKind ReadKind(FalloutPluginRecord furniture)
    {
        if (furniture.Signature != "FURN") throw new InvalidDataException("Furniture source is not FURN.");
        var data = furniture.ReadSubrecords().Single(field => field.Signature == "MNAM").Data;
        if (data.Length != 4) throw new InvalidDataException("Furniture marker flags have an invalid extent.");
        return (BinaryPrimitives.ReadUInt32LittleEndian(data.Span) & 0xc0000000u) switch
        {
            0x40000000u => FalloutPlayerFurnitureKind.Sitting,
            0x80000000u => FalloutPlayerFurnitureKind.Sleeping,
            _ => throw new NotSupportedException("Furniture requires another source procedure kind."),
        };
    }
    internal static FalloutFurnitureSeat Read(FalloutPluginStack stack, FalloutPluginRecord furniture,
        FalloutNifFile nif)
    {
        var seats = ReadSeats(stack, furniture, nif);
        return seats.Count == 1 ? seats[0] : throw new NotSupportedException("Furniture requires multi-marker approach/occupancy selection.");
    }

    internal static FalloutFurnitureSeat ReadPlayer(FalloutPluginStack stack, FalloutPluginRecord furniture, FalloutNifFile nif)
    {
        var seats = ReadSeats(stack, furniture, nif, true);
        return seats.Count == 1 ? seats[0] : throw new NotSupportedException("Player furniture requires multi-marker approach/occupancy selection.");
    }

    internal static IReadOnlyList<FalloutFurnitureSeat> ReadSeats(FalloutPluginStack stack, FalloutPluginRecord furniture,
        FalloutNifFile nif, bool allowSleeping = false, bool sourcePending = false)
    {
        if (furniture.Signature != "FURN") throw new InvalidDataException("Furniture source is not FURN.");
        var data = furniture.ReadSubrecords().Single(field => field.Signature == "MNAM").Data;
        if (data.Length != 4) throw new InvalidDataException("Furniture marker flags have an invalid extent.");
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(data.Span);
        var kind = sourcePending ? FalloutPendingFurnitureSource.Kind(flags) : ReadKind(furniture);
        if (kind == FalloutPlayerFurnitureKind.Sleeping && !allowSleeping)
            throw new NotSupportedException("Furniture requires its source sleep procedure owner.");
        var tables = nif.Blocks.Where(block => block.TypeName == "BSFurnitureMarker")
            .Select(block => (FalloutNifFurnitureMarker)nif.ReadObject(block.Index)).ToArray();
        if (tables.Length != 1) throw new NotSupportedException("Furniture marker table is absent or ambiguous.");
        var enabled = Enumerable.Range(0, 30).Where(index => (flags & (1u << index)) != 0).ToArray();
        if (enabled.Length == 0) throw new InvalidDataException("Furniture has no enabled source seats.");
        return enabled.Select(Seat).ToArray();

        FalloutFurnitureSeat Seat(int selected)
        {
            if (selected >= tables[0].Positions.Length) throw new InvalidDataException("Furniture mask exceeds the source marker table.");
            var marker = tables[0].Positions[selected];
            if (marker.PositionReference1 != marker.PositionReference2)
                throw new NotSupportedException("Furniture marker references require distinct entry/exit ownership.");
            var prefix = $"fFurnitureMarker{marker.PositionReference1:00}";
            return new(furniture.FormKey, selected, marker.PositionReference1, marker,
                [Setting("DeltaX"), Setting("DeltaY"), Setting("DeltaZ")], Setting("HeadingDelta"), kind);

            float Setting(string suffix)
            {
                var record = FalloutDialogueTopic.Find(stack, "GMST", prefix + suffix);
                var payload = record.ReadSubrecords().Single(field => field.Signature == "DATA").Data;
                if (payload.Length != 4) throw new InvalidDataException("Furniture placement GMST has an invalid extent.");
                var value = BinaryPrimitives.ReadSingleLittleEndian(payload.Span);
                if (!float.IsFinite(value)) throw new InvalidDataException("Furniture placement GMST is not finite.");
                return value;
            }
        }
    }
}
