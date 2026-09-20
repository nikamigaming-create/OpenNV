using System.Buffers.Binary;
using OpenNV.Runtime.Content;

internal static class AuthoredRagdollContracts
{
    internal static void Run()
    {
        var bytes = new byte[56];
        for (var index = 0; index < 2; index++)
        {
            var offset = index * 28;
            bytes[offset] = 6; bytes[offset + 1] = 0xA5;
            for (var axis = 0; axis < 6; axis++)
                BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset + 4 + axis * 4), index * 10 + axis - 2.5f);
        }
        var poses = FalloutAuthoredRagdoll.Decode(bytes);
        if (poses.Count != 2 || poses.Any(pose => pose.Part != 6) || poses[0].Position[0] != -2.5f ||
            poses[1].Position[2] != 9.5f || poses[1].RotationRadians[2] != 12.5f)
            throw new InvalidDataException("Authored pose lost duplicate part order, ignored padding, local units or angles.");
        Reject([]); Reject(bytes[..^1]);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(16), float.NaN); Reject(bytes);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(16), 0);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(4), float.PositiveInfinity); Reject(bytes);
    }

    private static void Reject(byte[] bytes)
    {
        try { _ = FalloutAuthoredRagdoll.Decode(bytes); }
        catch (InvalidDataException) { return; }
        throw new InvalidDataException("Malformed authored ragdoll was accepted.");
    }
}
