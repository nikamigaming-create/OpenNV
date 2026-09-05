using System.Buffers.Binary;

namespace OpenNV.Runtime.Formats.FaceGen;

internal static class FalloutFaceGenCoefficients
{
    internal static float[] AddSourceGeometry(ReadOnlySpan<byte> npc, ReadOnlySpan<byte> race, int modes)
    {
        if (modes < 0 || npc.Length != (long)modes * sizeof(float) || race.Length != npc.Length)
            throw new InvalidDataException("NPC and RACE FaceGen geometry coefficients must cover every EGM mode.");
        var output = new float[modes];
        for (var index = 0; index < output.Length; index++)
        {
            var npcValue = BinaryPrimitives.ReadSingleLittleEndian(npc[(index * sizeof(float))..]);
            var raceValue = BinaryPrimitives.ReadSingleLittleEndian(race[(index * sizeof(float))..]);
            var value = npcValue + raceValue;
            if (!float.IsFinite(npcValue) || !float.IsFinite(raceValue) || !float.IsFinite(value))
                throw new InvalidDataException("NPC/RACE FaceGen geometry coefficient is non-finite.");
            output[index] = value;
        }
        return output;
    }
}
