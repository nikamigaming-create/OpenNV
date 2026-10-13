using System.Runtime.CompilerServices;

namespace OpenNV.Runtime.Content;

// Reference distance uses three rounded Float32 subtractions and products,
// then Y+X followed by Z, before converting to double for square root.
internal static class FalloutActorDistanceMath
{
    internal static float Between(float[] actor, float[] target)
    {
        if (actor is not { Length: 3 } || target is not { Length: 3 })
            throw new InvalidDataException("Actor distance lost its three source position cells.");
        var x = Subtract(target[0], actor[0]); var y = Subtract(target[1], actor[1]); var z = Subtract(target[2], actor[2]);
        var squared = Add(Add(Multiply(y, y), Multiply(x, x)), Multiply(z, z));
        return (float)Math.Sqrt((double)squared);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static float Subtract(float left, float right) => left - right;
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static float Multiply(float left, float right) => left * right;
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static float Add(float left, float right) => left + right;
}
