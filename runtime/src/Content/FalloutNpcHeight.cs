using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

// The stored NPC height is independent of reference SetScale. Zero in NAM6
// inherits the sex-specific race height during source initialization.
internal static class FalloutNpcHeight
{
    internal static float Normalize(ReadOnlySpan<byte> source, float raceHeight)
    {
        if (source.Length is not (0 or 4)) throw new InvalidDataException("NPC height extent is invalid.");
        var value = source.IsEmpty ? 0 : BinaryPrimitives.ReadSingleLittleEndian(source);
        return Require(value == 0 ? raceHeight : value);
    }

    internal static float Race(FalloutPluginRecord race, bool female)
    {
        var fields = race.ReadSubrecords().Where(field => field.Signature == "DATA").ToArray();
        if (race.Signature != "RACE" || fields.Length != 1 || fields[0].Data.Length != 36)
            throw new InvalidDataException("NPC height requires winning RACE DATA.");
        return Require(BinaryPrimitives.ReadSingleLittleEndian(fields[0].Data.Span[(female ? 20 : 16)..]));
    }

    internal static float Require(float value) => float.IsFinite(value) && value > 0 ? value :
        throw new InvalidDataException("NPC stored height must be finite and positive.");
}
