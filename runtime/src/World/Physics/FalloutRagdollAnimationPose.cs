using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Physics;

internal sealed record FalloutRagdollAnimationPose(FalloutPhysicalVector3 Translation, FalloutNifQuaternion Rotation, uint Scale)
{
    internal void Validate()
    {
        if (!float.IsFinite(Translation.XValue) || !float.IsFinite(Translation.YValue) || !float.IsFinite(Translation.ZValue) ||
            !float.IsFinite(Rotation.W) || !float.IsFinite(Rotation.X) || !float.IsFinite(Rotation.Y) || !float.IsFinite(Rotation.Z) ||
            !float.IsFinite(BitConverter.UInt32BitsToSingle(Scale)) || BitConverter.UInt32BitsToSingle(Scale) <= 0)
            throw new InvalidDataException("Ragdoll pose omitted a finite current physical transform or positive scale.");
    }
    internal static FalloutRagdollAnimationPose Read(FalloutNifTransform local, float inheritedScale)
    {
        FalloutNifSceneMath.Validate(local);
        var m = local.RotationRowMajor;
        float w; var vector = new float[3];
        var trace = Add(Add(m[0], m[4]), m[8]);
        if (trace > 0)
        {
            var root = Root(Add(trace, 1)); var factor = Divide(.5f, root);
            w = Multiply(root, .5f);
            vector[0] = Multiply(Subtract(m[7], m[5]), factor);
            vector[1] = Multiply(Subtract(m[2], m[6]), factor);
            vector[2] = Multiply(Subtract(m[3], m[1]), factor);
        }
        else
        {
            var axis = m[4] > m[0] ? 1 : 0;
            if (m[8] > m[axis * 4]) axis = 2;
            var next = (axis + 1) % 3; var last = (next + 1) % 3;
            var root = Root(Add(Subtract(Subtract(m[axis * 4], m[next * 4]), m[last * 4]), 1));
            var factor = Divide(.5f, root); vector[axis] = Multiply(root, .5f);
            w = Multiply(Subtract(m[last * 3 + next], m[next * 3 + last]), factor);
            vector[next] = Multiply(Add(m[axis * 3 + next], m[next * 3 + axis]), factor);
            vector[last] = Multiply(Add(m[axis * 3 + last], m[last * 3 + axis]), factor);
        }
        var lengthSquared = Add(Add(Add(Multiply(vector[0], vector[0]), Multiply(vector[1], vector[1])),
            Multiply(vector[2], vector[2])), Multiply(w, w));
        if (!float.IsFinite(lengthSquared) || lengthSquared <= 0)
            throw new InvalidDataException("Ragdoll animation local rotation has no finite normalized quaternion.");
        var inverse = Divide(1, Root(lengthSquared));
        var rotation = new FalloutNifQuaternion(Multiply(w, inverse), Multiply(vector[0], inverse), Multiply(vector[1], inverse), Multiply(vector[2], inverse));
        var units = BitConverter.UInt32BitsToSingle(FalloutPhysicalConstructorInputs.SourceToPhysicalBits);
        var translation = local.Translation;
        return new(FalloutPhysicalVector3.FromValues(Multiply(Multiply(translation.X, units), inheritedScale),
            Multiply(Multiply(translation.Y, units), inheritedScale), Multiply(Multiply(translation.Z, units), inheritedScale)), rotation,
            BitConverter.SingleToUInt32Bits(local.Scale));
    }
    private static float Root(float value) => (float)Math.Sqrt(value);
    private static float Add(float a, float b) => FalloutPhysicalFloat32.Add(a, b);
    private static float Subtract(float a, float b) => FalloutPhysicalFloat32.Subtract(a, b);
    private static float Multiply(float a, float b) => FalloutPhysicalFloat32.Multiply(a, b);
    private static float Divide(float a, float b) => FalloutPhysicalFloat32.Divide(a, b);
}
