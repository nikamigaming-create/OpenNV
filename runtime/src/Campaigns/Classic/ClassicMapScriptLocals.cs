using System.Buffers.Binary;

namespace OpenNV.Runtime.Campaigns.Classic;

/// <summary>Projects an object's MAP local block into the VM's per-instance sparse storage.</summary>
internal static class ClassicMapScriptLocals
{
    internal static Dictionary<int, int> Read(ReadOnlySpan<byte> map, int localOffset, int count)
    {
        const int headerBytes = 0xec;
        if (map.Length < headerBytes) throw new InvalidDataException("MAP variable header is truncated.");
        var total = BinaryPrimitives.ReadInt32BigEndian(map[32..]);
        var mapCount = BinaryPrimitives.ReadInt32BigEndian(map[48..]);
        if (total < 0 || mapCount < 0 || (long)total + mapCount > (map.Length - headerBytes) / 4)
            throw new InvalidDataException("MAP variable arrays exceed their source bytes.");
        if (count < 0 || localOffset < -1 || localOffset >= 0 && (localOffset > total || count > total - localOffset))
            throw new InvalidDataException("Script locals exceed MAP variables.");
        var values = new Dictionary<int, int>();
        // -1 is a valid, not-yet-allocated block even with a positive declared
        // count. The VM materializes absent values as zero on access, then
        // persists writes with this script instance. Never read an adjacent
        // map variable as the value of an unallocated local.
        if (localOffset == -1) return values;
        for (var index = 0; index < count; index++)
            values.Add(index, BinaryPrimitives.ReadInt32BigEndian(map[(headerBytes + (mapCount + localOffset + index) * 4)..]));
        return values;
    }
}
