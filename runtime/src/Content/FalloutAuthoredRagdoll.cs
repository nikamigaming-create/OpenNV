using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutAuthoredRagdollBone(byte Part, float[] Position, float[] RotationRadians);

// XRGD contains ordered local NiNode transforms in game units, not Havok
// world-body transforms. Part numbers can repeat within the same skeleton.
internal sealed record FalloutAuthoredRagdoll(IReadOnlyList<FalloutAuthoredRagdollBone> Bones, float[]? BipedRotation)
{
    internal static FalloutAuthoredRagdoll? Read(FalloutPluginRecord record)
    {
        var fields = record.ReadSubrecords().Where(field => field.Signature is "XRGD" or "XRGB").ToArray();
        if (fields.Length == 0) return null;
        if (fields.Count(field => field.Signature == "XRGD") != 1 || fields.Count(field => field.Signature == "XRGB") > 1)
            throw new InvalidDataException($"Reference {record.FormKey} has ambiguous authored ragdoll fields.");
        var bones = Decode(fields.Single(field => field.Signature == "XRGD").Data.Span);
        var rotation = fields.SingleOrDefault(field => field.Signature == "XRGB");
        return new(bones, rotation.Signature is null ? null : ReadRotation(rotation.Data.Span));
    }

    internal static IReadOnlyList<FalloutAuthoredRagdollBone> Decode(ReadOnlySpan<byte> data)
    {
        const int stride = 28;
        if (data.Length == 0 || data.Length % stride != 0)
            throw new InvalidDataException("XRGD must contain complete 28-byte bone transforms.");
        var result = new FalloutAuthoredRagdollBone[data.Length / stride];
        for (var index = 0; index < result.Length; index++)
        {
            var row = data.Slice(index * stride, stride);
            // Bytes 1..3 are unused exporter padding, not a larger bone id.
            result[index] = new(row[0], Vector(row[4..16]), Vector(row[16..28]));
        }
        return result;
    }

    private static float[] ReadRotation(ReadOnlySpan<byte> data) => data.Length == 12 ? Vector(data) :
        throw new InvalidDataException("XRGB must contain three biped rotation angles.");

    private static float[] Vector(ReadOnlySpan<byte> data)
    {
        var result = new float[3];
        for (var axis = 0; axis < result.Length; axis++)
        {
            result[axis] = BinaryPrimitives.ReadSingleLittleEndian(data[(axis * sizeof(float))..]);
            if (!float.IsFinite(result[axis])) throw new InvalidDataException("Authored ragdoll transform is nonfinite.");
        }
        return result;
    }
}
