using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal static class FalloutActorLevel
{
    internal static int Resolve(ReadOnlySpan<byte> acbs, int? playerLevel)
    {
        if (acbs.Length != 24) throw new InvalidDataException("Actor level configuration has invalid extent.");
        var authored = BinaryPrimitives.ReadUInt16LittleEndian(acbs[8..]);
        if ((BinaryPrimitives.ReadUInt32LittleEndian(acbs) & 0x80) == 0) return Math.Max(1, (int)authored);
        if (playerLevel is null) throw new NotSupportedException("Scaled actor requires its persistent player-level selection.");
        if (playerLevel is < 1 or > ushort.MaxValue) throw new InvalidDataException("Actor selection level is invalid.");
        // The scaled level truncates before the optional minimum/maximum gates.
        var scaled = (int)(playerLevel.Value * (double)(authored / 1000f));
        if (scaled > short.MaxValue) throw new NotSupportedException("Actor scaled level exceeds the supported signed level range.");
        var minimum = BinaryPrimitives.ReadUInt16LittleEndian(acbs[10..]);
        var maximum = BinaryPrimitives.ReadUInt16LittleEndian(acbs[12..]);
        if (minimum > 0 && scaled < minimum) scaled = minimum;
        else if (maximum > 0 && scaled > maximum) scaled = maximum;
        return Math.Max(1, scaled);
    }
}
