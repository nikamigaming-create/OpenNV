using System.Buffers.Binary;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

internal static class FalloutPendingFurnitureSource
{
    internal static FalloutPlayerFurnitureKind Kind(uint flags) => (flags & 0x80000000u) != 0 ?
        FalloutPlayerFurnitureKind.Sleeping : FalloutPlayerFurnitureKind.Sitting;
    internal static uint Flags(FalloutPluginRecord furniture)
    {
        if (furniture.IsDeleted || furniture.Signature != "FURN")
            throw new InvalidDataException("Pending furniture flags require the effective FURN winner.");
        var fields = furniture.ReadSubrecords().Where(field => field.Signature == "MNAM").ToArray();
        if (fields.Length != 1 || fields[0].Data.Length != 4)
            throw new InvalidDataException("Pending furniture flags have an absent/ambiguous source extent.");
        return BinaryPrimitives.ReadUInt32LittleEndian(fields[0].Data.Span);
    }
    internal static int FirstMarker(uint enabled, uint used, uint reserved, bool ignoreReserved)
    {
        for (var marker = 0; marker < 30; marker++)
        {
            var bit = 1u << marker;
            if ((enabled & bit) != 0 && (used & bit) == 0 && (ignoreReserved || (reserved & bit) == 0)) return marker;
        }
        return -1;
    }
    internal static FalloutFurnitureSeat Marker(FalloutPluginStack records, FalloutPluginRecord furniture,
        FalloutNifFile nif, int marker) => FalloutFurnitureSource.ReadSeats(records, furniture, nif, allowSleeping: true, sourcePending: true)
        .SingleOrDefault(seat => seat.Index == marker) ??
            throw new InvalidDataException("Selected pending furniture marker is not in its original enabled source table.");
}
