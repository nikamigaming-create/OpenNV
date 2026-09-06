using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal static class FalloutRaceProperties
{
    internal static bool IsChild(FalloutPluginRecord race) => (Flags(race) & 4) != 0;
    internal static bool IsPlayable(FalloutPluginRecord race) => (Flags(race) & 1) != 0;

    private static uint Flags(FalloutPluginRecord race)
    {
        if (race.Signature != "RACE") throw new InvalidDataException("Race properties require a RACE record.");
        return ReadFlags(race.ReadSubrecords().Single(field => field.Signature == "DATA").Data.Span);
    }

    internal static uint ReadFlags(ReadOnlySpan<byte> data)
    {
        // https://tes5edit.github.io/fopdoc/FalloutNV/Records/RACE.html
        if (data.Length != 36) throw new InvalidDataException("RACE.DATA has an unsupported extent.");
        return BinaryPrimitives.ReadUInt32LittleEndian(data[32..]);
    }
}
