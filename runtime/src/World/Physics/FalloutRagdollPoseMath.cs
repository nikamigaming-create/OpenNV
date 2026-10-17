using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Physics;

// Uniform-scale reference poses stay in the C# source coordinate system.
// Each operation uses scalar Float32 stores, independently of Godot bases.
internal static class FalloutRagdollPoseMath
{
    internal static FalloutRagdollAnimationPose Compose(FalloutRagdollAnimationPose parent, FalloutRagdollAnimationPose child)
    {
        var p = parent.Rotation; var c = child.Rotation;
        var rotation = new FalloutNifQuaternion(
            Subtract(Subtract(Subtract(Multiply(p.W, c.W), Multiply(p.X, c.X)), Multiply(p.Y, c.Y)), Multiply(p.Z, c.Z)),
            Subtract(Add(Add(Multiply(p.W, c.X), Multiply(p.X, c.W)), Multiply(p.Y, c.Z)), Multiply(p.Z, c.Y)),
            Add(Add(Subtract(Multiply(p.W, c.Y), Multiply(p.X, c.Z)), Multiply(p.Y, c.W)), Multiply(p.Z, c.X)),
            Add(Subtract(Add(Multiply(p.W, c.Z), Multiply(p.X, c.Y)), Multiply(p.Y, c.X)), Multiply(p.Z, c.W)));
        var translation = Rotate(Scale(child.Translation, Value(parent.Scale)), p);
        return new(Add(parent.Translation, translation), rotation, Bits(Multiply(Value(parent.Scale), Value(child.Scale))));
    }
    internal static FalloutRagdollAnimationPose Relative(FalloutRagdollAnimationPose parent, FalloutRagdollAnimationPose child)
    {
        var q = parent.Rotation; var inverseScale = Divide(1, Value(parent.Scale));
        var inverse = new FalloutRagdollAnimationPose(default, new(q.W, -q.X, -q.Y, -q.Z), Bits(inverseScale));
        var delta = new FalloutPhysicalVector3(Bits(Subtract(child.Translation.XValue, parent.Translation.XValue)),
            Bits(Subtract(child.Translation.YValue, parent.Translation.YValue)), Bits(Subtract(child.Translation.ZValue, parent.Translation.ZValue)));
        var result = Compose(inverse, child with { Translation = delta });
        return result;
    }
    internal static IReadOnlyList<FalloutRagdollAnimationPose> Worlds(IReadOnlyList<int> parents, IReadOnlyList<FalloutRagdollAnimationPose> local)
    {
        if (parents.Count != local.Count) throw new InvalidDataException("Ragdoll reference pose count changed its skeleton parent array.");
        var result = new List<FalloutRagdollAnimationPose>(parents.Count);
        for (var index = 0; index < parents.Count; ++index)
        {
            var parent = parents[index];
            if (parent < -1 || parent >= index) throw new InvalidDataException("Ragdoll reference pose has an unordered parent.");
            result.Add(parent < 0 ? local[index] : Compose(result[parent], local[index]));
        }
        return result.AsReadOnly();
    }
    private static FalloutPhysicalVector3 Rotate(FalloutPhysicalVector3 value, FalloutNifQuaternion q)
    {
        var dot = Add(Add(Multiply(value.XValue, q.X), Multiply(value.YValue, q.Y)), Multiply(value.ZValue, q.Z));
        var diagonal = Subtract(Multiply(q.W, q.W), .5f);
        return FalloutPhysicalVector3.FromValues(
            Multiply(2, Add(Add(Multiply(dot, q.X), Multiply(diagonal, value.XValue)), Multiply(q.W, Subtract(Multiply(q.Y, value.ZValue), Multiply(q.Z, value.YValue))))),
            Multiply(2, Add(Add(Multiply(dot, q.Y), Multiply(diagonal, value.YValue)), Multiply(q.W, Subtract(Multiply(q.Z, value.XValue), Multiply(q.X, value.ZValue))))),
            Multiply(2, Add(Add(Multiply(dot, q.Z), Multiply(diagonal, value.ZValue)), Multiply(q.W, Subtract(Multiply(q.X, value.YValue), Multiply(q.Y, value.XValue))))));
    }
    private static FalloutPhysicalVector3 Scale(FalloutPhysicalVector3 value, float scale) =>
        FalloutPhysicalVector3.FromValues(Multiply(value.XValue, scale), Multiply(value.YValue, scale), Multiply(value.ZValue, scale));
    private static FalloutPhysicalVector3 Add(FalloutPhysicalVector3 a, FalloutPhysicalVector3 b) =>
        FalloutPhysicalVector3.FromValues(Add(a.XValue, b.XValue), Add(a.YValue, b.YValue), Add(a.ZValue, b.ZValue));
    private static uint Bits(float value) => BitConverter.SingleToUInt32Bits(value);
    private static float Value(uint bits) => BitConverter.UInt32BitsToSingle(bits);
    private static float Add(float a, float b) => FalloutPhysicalFloat32.Add(a, b);
    private static float Subtract(float a, float b) => FalloutPhysicalFloat32.Subtract(a, b);
    private static float Multiply(float a, float b) => FalloutPhysicalFloat32.Multiply(a, b);
    private static float Divide(float a, float b) => FalloutPhysicalFloat32.Divide(a, b);
}
