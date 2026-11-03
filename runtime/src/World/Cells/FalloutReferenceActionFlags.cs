using System.Buffers.Binary;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal static class FalloutReferenceActionFlags
{
    internal static bool InitialOpen(FalloutPluginRecord reference)
    {
        // Source action data defaults to one. The native loader replaces its
        // low byte for each XACT; bit four is the logical open-state query.
        byte flags = 1;
        foreach (var field in reference.ReadSubrecords().Where(field => field.Signature == "XACT"))
        {
            if (field.Data.Length != 4)
                throw new InvalidDataException($"Reference {reference.FormKey} XACT must contain a uint32 action declaration.");
            flags = unchecked((byte)BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span));
        }
        return (flags & 4) != 0;
    }
}
